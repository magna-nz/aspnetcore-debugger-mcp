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

    // Contributed by gloath.com
    /// Remove and return the buffered lines matching `category` (all categories when null),
    /// up to `maxLines`. Lines that do not match, and matching lines beyond `maxLines`, stay
    /// in the buffer in their original relative order — draining stderr must not throw away
    /// the stdout a caller has not read yet.
    public IReadOnlyList<OutputLine> Drain(string? category = null, int? maxLines = null)
    {
        var collected = new List<OutputLine>();
        lock (_gate)
        {
            // Rotate the queue exactly once: each item is dequeued, then either collected or
            // re-enqueued at the tail. After a full pass the kept items are back in their
            // original relative order, so an early exit is not safe here.
            int count = _items.Count;
            for (int i = 0; i < count; i++)
            {
                var line = _items.Dequeue();
                bool matches = category is null
                    || string.Equals(line.Category, category, StringComparison.OrdinalIgnoreCase);
                bool underLimit = maxLines is not int m || collected.Count < m;

                if (matches && underLimit)
                {
                    _currentBytes -= EstimateSize(line);
                    collected.Add(line);
                }
                else
                {
                    _items.Enqueue(line);
                }
            }
        }
        return collected;
    }

    /// Non-destructive snapshot of the most recent matching lines, in chronological
    /// order (oldest first). Used by composite tools (e.g. breakpoint_wait) that want
    /// to surface "what just printed" without disturbing a caller's drain pattern.
    public IReadOnlyList<OutputLine> PeekRecent(int maxLines, string? category = null)
    {
        if (maxLines <= 0) return Array.Empty<OutputLine>();
        lock (_gate)
        {
            if (_items.Count == 0) return Array.Empty<OutputLine>();
            IEnumerable<OutputLine> source = _items;
            if (category is not null)
                source = source.Where(l => string.Equals(l.Category, category, StringComparison.OrdinalIgnoreCase));
            // Materialise inside the lock; the queue may mutate the moment we release it.
            var filtered = source as IReadOnlyCollection<OutputLine> ?? source.ToList();
            if (filtered.Count <= maxLines) return filtered.ToList();
            return filtered.Skip(filtered.Count - maxLines).ToList();
        }
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
