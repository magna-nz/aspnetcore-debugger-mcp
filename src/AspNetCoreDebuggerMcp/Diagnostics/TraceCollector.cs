using AspNetCoreDebuggerMcp.Inspection;

namespace AspNetCoreDebuggerMcp.Diagnostics;

/// Per-session trace state. Single active trace at a time. Owns:
///  - the set of function names being traced (used at start to set BPs)
///  - the adapter-issued BP ids (used to recognise trace hits in DAP stopped events)
///  - whether exception traces are enabled
///  - the captured TraceEvent log
internal sealed class TraceCollector
{
    private readonly object _gate = new();
    private TraceSession? _current;

    public bool IsActive { get { lock (_gate) return _current is not null; } }

    public TraceConfig Start(IReadOnlyList<string> methods, bool captureStack, bool captureLocals,
        bool includeExceptions, int maxFramesPerEvent, int maxLocalsPerFrame)
    {
        lock (_gate)
        {
            if (_current is not null)
                throw new InvalidOperationException(
                    "A trace is already active. Call trace_stop before starting another.");
            _current = new TraceSession(
                Methods: methods.ToList(),
                AdapterIds: new HashSet<int>(),
                IncludeExceptions: includeExceptions,
                CaptureStack: captureStack,
                CaptureLocals: captureLocals,
                MaxFramesPerEvent: maxFramesPerEvent,
                MaxLocalsPerFrame: maxLocalsPerFrame,
                Started: DateTimeOffset.UtcNow,
                Events: new List<TraceEvent>());
            return _current.AsConfig();
        }
    }

    public TraceConfig? Stop()
    {
        lock (_gate)
        {
            var c = _current?.AsConfig();
            _current = null;
            return c;
        }
    }

    public IReadOnlyList<string> TracedMethods()
    {
        lock (_gate)
            return _current?.Methods.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public void SetAdapterIds(IEnumerable<int> adapterIds)
    {
        lock (_gate)
        {
            if (_current is null) return;
            _current.AdapterIds.Clear();
            foreach (var id in adapterIds) _current.AdapterIds.Add(id);
        }
    }

    public bool MatchesAnyTraceBp(IEnumerable<int> hitBreakpointIds)
    {
        lock (_gate)
        {
            if (_current is null) return false;
            foreach (var id in hitBreakpointIds)
                if (_current.AdapterIds.Contains(id)) return true;
            return false;
        }
    }

    public bool ExceptionTracingEnabled
    {
        get { lock (_gate) return _current is { IncludeExceptions: true }; }
    }

    public (bool CaptureStack, bool CaptureLocals, int MaxFrames, int MaxLocals)? CaptureOptions
    {
        get
        {
            lock (_gate)
            {
                if (_current is null) return null;
                return (_current.CaptureStack, _current.CaptureLocals,
                        _current.MaxFramesPerEvent, _current.MaxLocalsPerFrame);
            }
        }
    }

    public void Append(TraceEvent ev)
    {
        lock (_gate) _current?.Events.Add(ev);
    }

    public long ElapsedMs(DateTimeOffset now)
    {
        lock (_gate)
            return _current is null ? 0 : (long)(now - _current.Started).TotalMilliseconds;
    }

    public IReadOnlyList<TraceEvent> Events(int? max = null)
    {
        lock (_gate)
        {
            if (_current is null) return Array.Empty<TraceEvent>();
            var list = _current.Events;
            if (max is int m && list.Count > m) return list.Skip(list.Count - m).ToList();
            return list.ToList();
        }
    }
}

internal sealed record TraceSession(
    List<string> Methods,
    HashSet<int> AdapterIds,
    bool IncludeExceptions,
    bool CaptureStack,
    bool CaptureLocals,
    int MaxFramesPerEvent,
    int MaxLocalsPerFrame,
    DateTimeOffset Started,
    List<TraceEvent> Events)
{
    public TraceConfig AsConfig() => new(Methods, IncludeExceptions, CaptureStack, CaptureLocals,
        MaxFramesPerEvent, MaxLocalsPerFrame);
}

public sealed record TraceConfig(
    IReadOnlyList<string> Methods,
    bool IncludeExceptions,
    bool CaptureStack,
    bool CaptureLocals,
    int MaxFramesPerEvent,
    int MaxLocalsPerFrame);
