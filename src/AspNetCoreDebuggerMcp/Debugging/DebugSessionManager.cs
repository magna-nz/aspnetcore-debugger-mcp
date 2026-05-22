using AspNetCoreDebuggerMcp.Breakpoints;
using AspNetCoreDebuggerMcp.Inspection;

namespace AspNetCoreDebuggerMcp.Debugging;

/// Combined result of a wait-for-stop call: the stop event, the current session snapshot,
/// and auto-context (top frame + source snippet) when the stopped thread is known.
public sealed record WaitResult(
    StopInfo Stop,
    SessionSnapshot Session,
    StackFrame? TopFrame,
    SourceSnippet? Snippet);

/// Holds the single active debug session. Session-lifecycle calls (launch/attach/disconnect)
/// are serialised under a semaphore so concurrent tool invocations cannot race. Within a session,
/// execution and breakpoint calls pass through directly — the session's own state is thread-safe.
public sealed class DebugSessionManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private DebugSession? _session;

    // ---- session lifecycle ----------------------------------------------------------

    public async Task<SessionSnapshot> LaunchAsync(
        string program, string[]? args, string? cwd, bool stopAtEntry, CancellationToken ct)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureNoActiveSession();
            await DisposeExistingAsync().ConfigureAwait(false);
            var path = NetcoredbgLocator.Locate();
            _session = await DebugSession.LaunchAsync(path, program, args, cwd, stopAtEntry, ct)
                .ConfigureAwait(false);
            return _session.Snapshot();
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task<SessionSnapshot> AttachAsync(int processId, CancellationToken ct)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureNoActiveSession();
            await DisposeExistingAsync().ConfigureAwait(false);
            var path = NetcoredbgLocator.Locate();
            _session = await DebugSession.AttachAsync(path, processId, ct).ConfigureAwait(false);
            return _session.Snapshot();
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task<SessionSnapshot> DisconnectAsync(CancellationToken ct)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null) return SessionSnapshot.None;
            try { await _session.DisconnectAsync(ct).ConfigureAwait(false); } catch { }
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
            return SessionSnapshot.None;
        }
        finally { _lifecycleGate.Release(); }
    }

    public SessionSnapshot GetState()
        => _session is null ? SessionSnapshot.None : _session.Snapshot();

    // ---- execution control ----------------------------------------------------------

    public async Task<SessionSnapshot> ContinueAsync(int? threadId, CancellationToken ct)
    {
        var s = RequireActiveSession();
        await s.ContinueAsync(threadId, ct).ConfigureAwait(false);
        return s.Snapshot();
    }

    public async Task<SessionSnapshot> PauseAsync(int? threadId, CancellationToken ct)
    {
        var s = RequireActiveSession();
        await s.PauseAsync(threadId, ct).ConfigureAwait(false);
        return s.Snapshot();
    }

    public async Task<SessionSnapshot> StepAsync(string kind, int? threadId, CancellationToken ct)
    {
        var s = RequireActiveSession();
        await s.StepAsync(kind, threadId, ct).ConfigureAwait(false);
        return s.Snapshot();
    }

    public async Task<WaitResult> WaitForStopAsync(TimeSpan timeout, CancellationToken ct)
    {
        var s = RequireActiveSession();
        var stop = await s.WaitForStopAsync(timeout, ct).ConfigureAwait(false);

        // Auto-context-on-stop: best-effort top frame + source snippet for the stopped thread.
        StackFrame? topFrame = null;
        SourceSnippet? snippet = null;
        if (stop.ThreadId is int tid)
        {
            try { topFrame = await s.GetTopFrameAsync(tid, ct).ConfigureAwait(false); }
            catch { /* best-effort; auto-context is opportunistic */ }

            if (topFrame is { SourcePath: { } path, Line: int line })
                snippet = InspectionService.TryReadSnippet(path, line);
        }

        return new WaitResult(stop, s.Snapshot(), topFrame, snippet);
    }

    // ---- inspection passthroughs ---------------------------------------------------

    public Task<IReadOnlyList<ThreadInfo>> ListThreadsAsync(CancellationToken ct)
        => RequireActiveSession().ListThreadsAsync(ct);

    public Task<IReadOnlyList<StackFrame>> GetStackTraceAsync(
        int? threadId, int? startFrame, int? levels, CancellationToken ct)
    {
        var s = RequireActiveSession();
        return s.GetStackTraceAsync(ResolveThreadIdOrThrow(threadId), startFrame, levels, ct);
    }

    public async Task<IReadOnlyList<ScopeWithVariables>> GetScopesAsync(
        int? frameId, int depth, int maxChildren, CancellationToken ct)
    {
        var s = RequireActiveSession();
        int fid = frameId ?? await ResolveTopFrameIdAsync(s, ct).ConfigureAwait(false);
        return await s.GetScopesAsync(fid, depth, maxChildren, ct).ConfigureAwait(false);
    }

    public Task<EvaluateResult> EvaluateAsync(string expression, int? frameId, CancellationToken ct)
        => RequireActiveSession().EvaluateAsync(expression, frameId, ct);

    public Task<EvaluateResult> SetExpressionAsync(
        string expression, string value, int? frameId, CancellationToken ct)
        => RequireActiveSession().SetExpressionAsync(expression, value, frameId, ct);

    public Task<ExceptionAutopsy> AutopsyAsync(int? threadId, int topFrameCount, CancellationToken ct)
    {
        var s = RequireActiveSession();
        return s.AutopsyAsync(ResolveThreadIdOrThrow(threadId), topFrameCount, ct);
    }

    private int ResolveThreadIdOrThrow(int? threadId)
    {
        if (threadId is int id) return id;
        if (_session?.LastStop?.ThreadId is int t) return t;
        throw new DebugException("No current thread known. Pass threadId explicitly, or wait for a stop first.");
    }

    private static async Task<int> ResolveTopFrameIdAsync(DebugSession s, CancellationToken ct)
    {
        if (s.LastStop?.ThreadId is not int tid)
            throw new DebugException("No current thread known. Pass frameId explicitly, or wait for a stop first.");
        var top = await s.GetTopFrameAsync(tid, ct).ConfigureAwait(false)
            ?? throw new DebugException("Could not determine the top frame.");
        return top.Id;
    }

    // ---- breakpoints ----------------------------------------------------------------

    public Task<LineBreakpoint> AddLineBreakpointAsync(
        string sourcePath, int line, string? condition, string? hitCondition, string? logMessage,
        CancellationToken ct)
        => RequireActiveSession().AddLineBreakpointAsync(sourcePath, line, condition, hitCondition, logMessage, ct);

    public Task<FunctionBreakpoint> AddFunctionBreakpointAsync(
        string functionName, string? condition, string? hitCondition, CancellationToken ct)
        => RequireActiveSession().AddFunctionBreakpointAsync(functionName, condition, hitCondition, ct);

    public Task<bool> RemoveBreakpointAsync(string id, CancellationToken ct)
        => RequireActiveSession().RemoveBreakpointAsync(id, ct);

    public Task SetExceptionFiltersAsync(IEnumerable<string> filters, CancellationToken ct)
        => RequireActiveSession().SetExceptionFiltersAsync(filters, ct);

    public BreakpointsSnapshot ListBreakpoints()
        => _session is null
            ? new BreakpointsSnapshot(
                Array.Empty<LineBreakpoint>(),
                Array.Empty<FunctionBreakpoint>(),
                Array.Empty<string>())
            : _session.BreakpointsSnapshot();

    // ---- helpers --------------------------------------------------------------------

    private DebugSession RequireActiveSession()
    {
        if (_session is null || _session.State == SessionState.Terminated)
            throw new DebugException(
                "No active debug session. Call debug_launch or debug_attach first.");
        return _session;
    }

    private void EnsureNoActiveSession()
    {
        if (_session is not null && _session.State != SessionState.Terminated)
            throw new DebugException(
                "A debug session is already active. Call debug_disconnect before starting a new one.");
    }

    private async Task DisposeExistingAsync()
    {
        if (_session is null) return;
        try { await _session.DisposeAsync().ConfigureAwait(false); } catch { }
        _session = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            try { await _session.DisposeAsync().ConfigureAwait(false); } catch { }
            _session = null;
        }
        _lifecycleGate.Dispose();
    }
}
