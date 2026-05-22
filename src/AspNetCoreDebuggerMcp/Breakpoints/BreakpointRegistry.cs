namespace AspNetCoreDebuggerMcp.Breakpoints;

/// In-memory store for the user's intended set of breakpoints. The DAP adapter requires
/// a full-replacement model (setBreakpoints overwrites all BPs for a source), so we keep
/// the authoritative intent here and re-send it on every mutation.
internal sealed class BreakpointRegistry
{
    private readonly object _gate = new();
    private readonly List<LineBreakpoint> _line = new();
    private readonly List<FunctionBreakpoint> _function = new();
    private readonly HashSet<string> _exceptionFilters = new(StringComparer.OrdinalIgnoreCase);

    public LineBreakpoint AddLine(
        string sourcePath, int line, string? condition, string? hitCondition, string? logMessage)
    {
        var bp = new LineBreakpoint(
            Id: NewId("bp"),
            SourcePath: sourcePath, Line: line,
            Condition: condition, HitCondition: hitCondition, LogMessage: logMessage,
            Verified: false, AdapterId: null);
        lock (_gate) _line.Add(bp);
        return bp;
    }

    public FunctionBreakpoint AddFunction(string functionName, string? condition, string? hitCondition)
    {
        var bp = new FunctionBreakpoint(
            Id: NewId("fbp"),
            FunctionName: functionName,
            Condition: condition, HitCondition: hitCondition,
            Verified: false, AdapterId: null);
        lock (_gate) _function.Add(bp);
        return bp;
    }

    /// Removes the breakpoint with the given id from any list it lives in.
    /// Returns the kind removed (line / function) or null if not found.
    public BreakpointKind? Remove(string id)
    {
        lock (_gate)
        {
            int i = _line.FindIndex(b => b.Id == id);
            if (i >= 0) { _line.RemoveAt(i); return BreakpointKind.Line; }
            int j = _function.FindIndex(b => b.Id == id);
            if (j >= 0) { _function.RemoveAt(j); return BreakpointKind.Function; }
            return null;
        }
    }

    public string? GetSourcePathOf(string id)
    {
        lock (_gate)
            return _line.FirstOrDefault(b => b.Id == id)?.SourcePath;
    }

    public IReadOnlyList<LineBreakpoint> ForSource(string sourcePath)
    {
        lock (_gate)
            return _line
                .Where(b => string.Equals(b.SourcePath, sourcePath, StringComparison.Ordinal))
                .ToList();
    }

    public IReadOnlyList<FunctionBreakpoint> AllFunction()
    {
        lock (_gate) return _function.ToList();
    }

    /// All distinct source paths currently holding at least one line breakpoint.
    public IReadOnlyList<string> Sources()
    {
        lock (_gate) return _line.Select(b => b.SourcePath).Distinct(StringComparer.Ordinal).ToList();
    }

    public void UpdateLine(string id, bool verified, int? adapterId)
    {
        lock (_gate)
        {
            int i = _line.FindIndex(b => b.Id == id);
            if (i >= 0) _line[i] = _line[i] with { Verified = verified, AdapterId = adapterId };
        }
    }

    public void UpdateFunction(string id, bool verified, int? adapterId)
    {
        lock (_gate)
        {
            int i = _function.FindIndex(b => b.Id == id);
            if (i >= 0) _function[i] = _function[i] with { Verified = verified, AdapterId = adapterId };
        }
    }

    public void UpdateLineByAdapterId(int adapterId, bool verified)
    {
        lock (_gate)
        {
            for (int i = 0; i < _line.Count; i++)
                if (_line[i].AdapterId == adapterId)
                    _line[i] = _line[i] with { Verified = verified };
        }
    }

    public void SetExceptionFilters(IEnumerable<string> filters)
    {
        lock (_gate)
        {
            _exceptionFilters.Clear();
            foreach (var f in filters)
                if (!string.IsNullOrWhiteSpace(f)) _exceptionFilters.Add(f);
        }
    }

    public BreakpointsSnapshot Snapshot()
    {
        lock (_gate)
            return new BreakpointsSnapshot(
                _line.ToList(),
                _function.ToList(),
                _exceptionFilters.ToList());
    }

    private static string NewId(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 1 + 8)];
}

internal enum BreakpointKind { Line, Function }
