using AspNetCoreDebuggerMcp.Diagnostics;

namespace AspNetCoreDebuggerMcp.Debugging;

/// Drop-oldest queue of DAP output lines, capped on both line count and total byte size.
/// Whichever cap is hit first triggers eviction. The byte cap exists so a single rare-but-
/// huge line (e.g. a multi-MB stack trace dump) can't fill memory on its own.
///
/// Memory safety motivation: see MAG-54 — debuggees in a hot Console.WriteLine path or an
/// exception-logging loop can emit hundreds of MB/s of output. Without a cap, the buffer
/// grew to ~38 GB in a few minutes during manual debugging.
internal sealed class BoundedOutputBuffer
{
    public const int DefaultMaxLines = 50_000;
    public const long DefaultMaxBytes = 64L * 1024 * 1024; // 64 MB

    private readonly object _gate = new();
    private readonly Queue<OutputLine> _items = new();
    private readonly int _maxLines;
    private readonly long _maxBytes;
    private long _currentBytes;
    private long _droppedLines;

    public BoundedOutputBuffer(int maxLines = DefaultMaxLines, long maxBytes = DefaultMaxBytes)
    {
        if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxLines = maxLines;
        _maxBytes = maxBytes;
    }

    public void Enqueue(OutputLine line)
    {
        var size = EstimateSize(line);
        lock (_gate)
        {
            _items.Enqueue(line);
            _currentBytes += size;

            while (_items.Count > 0 && (_items.Count > _maxLines || _currentBytes > _maxBytes))
            {
                var dropped = _items.Dequeue();
                _currentBytes -= EstimateSize(dropped);
                _droppedLines++;
            }
        }
    }

    public IReadOnlyList<OutputLine> Drain(string? category = null, int? maxLines = null)
    {
        var collected = new List<OutputLine>();
        lock (_gate)
        {
            while (_items.Count > 0)
            {
                var line = _items.Dequeue();
                _currentBytes -= EstimateSize(line);
                if (category is null || string.Equals(line.Category, category, StringComparison.OrdinalIgnoreCase))
                    collected.Add(line);
                if (maxLines is int m && collected.Count >= m) break;
            }
        }
        return collected;
    }

    public OutputBufferStats Snapshot()
    {
        lock (_gate)
            return new OutputBufferStats(_items.Count, _currentBytes, _droppedLines, _maxLines, _maxBytes);
    }

    /// Rough in-memory size of an OutputLine. Chars are 2 bytes in .NET strings; the
    /// constant accounts for the record object header, the DateTimeOffset, and the
    /// category string (typically "stdout"/"stderr"/"console", a handful of bytes interned).
    private static long EstimateSize(OutputLine line)
        => (long)(line.Output?.Length ?? 0) * 2 + 80;
}

public sealed record OutputBufferStats(
    int Lines,
    long Bytes,
    long DroppedLines,
    int MaxLines,
    long MaxBytes);
