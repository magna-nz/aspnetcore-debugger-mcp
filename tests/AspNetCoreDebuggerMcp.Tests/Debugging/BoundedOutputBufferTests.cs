using AspNetCoreDebuggerMcp.Debugging;
using AspNetCoreDebuggerMcp.Diagnostics;

namespace AspNetCoreDebuggerMcp.Tests.Debugging;

public class BoundedOutputBufferTests
{
    [Fact]
    public void Enqueue_BelowCaps_KeepsEverything()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        for (int i = 0; i < 10; i++)
            buf.Enqueue(Line($"line-{i}"));

        var stats = buf.Snapshot();
        Assert.Equal(10, stats.Lines);
        Assert.Equal(0, stats.DroppedLines);
    }

    [Fact]
    public void Enqueue_AboveLineCap_DropsOldest()
    {
        var buf = new BoundedOutputBuffer(maxLines: 5, maxBytes: 1_000_000);
        for (int i = 0; i < 8; i++)
            buf.Enqueue(Line($"line-{i}"));

        var stats = buf.Snapshot();
        Assert.Equal(5, stats.Lines);
        Assert.Equal(3, stats.DroppedLines);

        var drained = buf.Drain();
        Assert.Equal(new[] { "line-3", "line-4", "line-5", "line-6", "line-7" },
            drained.Select(l => l.Output));
    }

    [Fact]
    public void Enqueue_AboveByteCap_DropsOldest()
    {
        // Each line below is ~10 chars × 2 bytes + 80 byte overhead ≈ 100 bytes.
        // maxBytes=300 should fit ~3 lines.
        var buf = new BoundedOutputBuffer(maxLines: 1000, maxBytes: 300);
        for (int i = 0; i < 10; i++)
            buf.Enqueue(Line($"ten-chars-{i}"));

        var stats = buf.Snapshot();
        Assert.True(stats.Bytes <= 300, $"bytes={stats.Bytes} exceeded cap=300");
        Assert.True(stats.DroppedLines > 0);
        // exactly: 10 lines * ~104 bytes = ~1040 bytes; cap drains to <=300 → keeps last 2-3.
        Assert.InRange(stats.Lines, 2, 4);
    }

    [Fact]
    public void Floods_DoNotGrowMemory_Bounded()
    {
        // The regression: simulate the MAG-54 scenario — 1,000,000 lines pumped at the
        // buffer. With a 50,000-line cap, only 50,000 should survive and the rest count
        // as dropped. (Total bytes here are well below the byte cap, so line cap dominates.)
        var buf = new BoundedOutputBuffer(maxLines: 50_000, maxBytes: 1024L * 1024 * 1024);
        for (int i = 0; i < 1_000_000; i++)
            buf.Enqueue(Line("x"));

        var stats = buf.Snapshot();
        Assert.Equal(50_000, stats.Lines);
        Assert.Equal(950_000, stats.DroppedLines);
    }

    [Fact]
    public void Drain_RemovesItems_AndResetsByteAccounting()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 10_000);
        for (int i = 0; i < 10; i++)
            buf.Enqueue(Line($"line-{i}"));

        var drained = buf.Drain();
        Assert.Equal(10, drained.Count);

        var stats = buf.Snapshot();
        Assert.Equal(0, stats.Lines);
        Assert.Equal(0, stats.Bytes);
    }

    [Fact]
    public void Drain_FilterByCategory_OnlyMatching()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        buf.Enqueue(new OutputLine("stdout", "a", DateTimeOffset.UtcNow));
        buf.Enqueue(new OutputLine("stderr", "b", DateTimeOffset.UtcNow));
        buf.Enqueue(new OutputLine("stdout", "c", DateTimeOffset.UtcNow));

        var stderrLines = buf.Drain(category: "stderr");
        Assert.Single(stderrLines);
        Assert.Equal("b", stderrLines[0].Output);
    }

    [Fact]
    public void Drain_WithMaxLines_StopsAtLimit()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        for (int i = 0; i < 10; i++)
            buf.Enqueue(Line($"line-{i}"));

        var first3 = buf.Drain(maxLines: 3);
        Assert.Equal(3, first3.Count);

        var rest = buf.Drain();
        Assert.Equal(7, rest.Count);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCaps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedOutputBuffer(maxLines: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundedOutputBuffer(maxLines: 100, maxBytes: 0));
    }

    /// MAG-54 regression: pump 200 MB of output through a 4 MB-capped buffer and assert
    /// that the heap allocation tied to the buffer stays bounded around the cap. Without
    /// the fix, this would balloon to ~200 MB; with the fix it should hover near the cap
    /// (well under 50 MB after a forced GC pass).
    [Fact]
    public void Flood_DoesNotGrowHeap_HardMemoryBound()
    {
        var buf = new BoundedOutputBuffer(maxLines: 10_000, maxBytes: 4L * 1024 * 1024);

        // Establish a baseline after the buffer is constructed.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var baseline = GC.GetTotalMemory(forceFullCollection: true);

        // Pump ~200 MB of output: 100,000 lines × ~2 KB each.
        var bigLine = new string('x', 1024); // 1024 chars × 2 bytes ≈ 2 KB per OutputLine
        for (int i = 0; i < 100_000; i++)
            buf.Enqueue(new OutputLine("stdout", bigLine, DateTimeOffset.UtcNow));

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var afterFlood = GC.GetTotalMemory(forceFullCollection: true);
        var grew = afterFlood - baseline;

        // Bound check: heap growth should be well under 50 MB even though we pumped 200 MB
        // of payload. The cap is 4 MB but GC + Queue<T> slack pushes this higher in practice.
        // Without the fix, growth would be ~200 MB.
        Assert.True(grew < 50L * 1024 * 1024,
            $"Heap grew by {grew / (1024.0 * 1024.0):F1} MB; expected < 50 MB under a 4 MB cap.");

        var stats = buf.Snapshot();
        Assert.True(stats.Bytes <= buf.Snapshot().MaxBytes, "byte count exceeded cap");
        Assert.True(stats.DroppedLines > 0, "expected drops under flood");
    }

    [Fact]
    public void PeekRecent_ReturnsLastN_InChronologicalOrder()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        for (int i = 0; i < 10; i++)
            buf.Enqueue(Line($"line-{i}"));

        var peek = buf.PeekRecent(maxLines: 3);
        Assert.Equal(new[] { "line-7", "line-8", "line-9" }, peek.Select(l => l.Output));
    }

    [Fact]
    public void PeekRecent_IsNonDestructive()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        for (int i = 0; i < 5; i++)
            buf.Enqueue(Line($"line-{i}"));

        _ = buf.PeekRecent(maxLines: 2);

        // A subsequent drain must see every line — peek must not have consumed any.
        var drained = buf.Drain();
        Assert.Equal(5, drained.Count);
        Assert.Equal(new[] { "line-0", "line-1", "line-2", "line-3", "line-4" },
            drained.Select(l => l.Output));
    }

    [Fact]
    public void PeekRecent_LargerThanBuffer_ReturnsAll()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        for (int i = 0; i < 3; i++)
            buf.Enqueue(Line($"line-{i}"));

        var peek = buf.PeekRecent(maxLines: 50);
        Assert.Equal(3, peek.Count);
        Assert.Equal(new[] { "line-0", "line-1", "line-2" }, peek.Select(l => l.Output));
    }

    [Fact]
    public void PeekRecent_RespectsCategoryFilter()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        buf.Enqueue(new OutputLine("stdout", "out-1", DateTimeOffset.UtcNow));
        buf.Enqueue(new OutputLine("stderr", "err-1", DateTimeOffset.UtcNow));
        buf.Enqueue(new OutputLine("stdout", "out-2", DateTimeOffset.UtcNow));
        buf.Enqueue(new OutputLine("stderr", "err-2", DateTimeOffset.UtcNow));
        buf.Enqueue(new OutputLine("stdout", "out-3", DateTimeOffset.UtcNow));

        var peek = buf.PeekRecent(maxLines: 2, category: "stderr");
        Assert.Equal(new[] { "err-1", "err-2" }, peek.Select(l => l.Output));
    }

    [Fact]
    public void PeekRecent_NonPositive_ReturnsEmpty()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        buf.Enqueue(Line("a"));

        Assert.Empty(buf.PeekRecent(maxLines: 0));
        Assert.Empty(buf.PeekRecent(maxLines: -5));

        // Buffer still intact.
        Assert.Equal(1, buf.Snapshot().Lines);
    }

    [Fact]
    public void PeekRecent_EmptyBuffer_ReturnsEmpty()
    {
        var buf = new BoundedOutputBuffer(maxLines: 100, maxBytes: 1_000_000);
        Assert.Empty(buf.PeekRecent(maxLines: 10));
    }

    private static OutputLine Line(string text) =>
        new OutputLine("stdout", text, DateTimeOffset.UtcNow);
}
