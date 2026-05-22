using System.Text.Json;
using AspNetCoreDebuggerMcp.Breakpoints;
using AspNetCoreDebuggerMcp.Dap;
using AspNetCoreDebuggerMcp.Inspection;

namespace AspNetCoreDebuggerMcp.Debugging;

/// One active debug session: owns the netcoredbg process and the DAP client,
/// runs the launch/attach handshake, manages breakpoints, drives execution control,
/// and translates DAP events into state transitions.
internal sealed class DebugSession : IAsyncDisposable
{
    private readonly NetcoredbgProcess _process;
    private readonly DapClient _client;
    private readonly SessionStateMachine _stateMachine = new();
    private readonly StopWaiter _stopWaiter = new();
    private readonly BreakpointRegistry _breakpoints = new();
    private readonly InspectionService _inspector;
    private readonly TaskCompletionSource _initializedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private int? _processId;
    private StopInfo? _lastStop;

    public SessionState State => _stateMachine.State;
    public int? ProcessId { get { lock (_gate) return _processId; } }
    public StopInfo? LastStop { get { lock (_gate) return _lastStop; } }

    public SessionSnapshot Snapshot() => new(State.ToString(), ProcessId, LastStop);
    public BreakpointsSnapshot BreakpointsSnapshot() => _breakpoints.Snapshot();

    private DebugSession(NetcoredbgProcess process, DapClient client)
    {
        _process = process;
        _client = client;
        _inspector = new InspectionService(client);
        _client.EventReceived += OnDapEvent;
    }

    // ---- inspection (delegated to InspectionService) ---------------------------------

    public Task<IReadOnlyList<ThreadInfo>> ListThreadsAsync(CancellationToken ct)
        => _inspector.ListThreadsAsync(ct);

    public Task<IReadOnlyList<StackFrame>> GetStackTraceAsync(
        int threadId, int? startFrame, int? levels, CancellationToken ct)
        => _inspector.GetStackTraceAsync(threadId, startFrame, levels, ct);

    public Task<StackFrame?> GetTopFrameAsync(int threadId, CancellationToken ct)
        => _inspector.GetTopFrameAsync(threadId, ct);

    public Task<IReadOnlyList<ScopeWithVariables>> GetScopesAsync(
        int frameId, int depth, int maxChildren, CancellationToken ct)
        => _inspector.GetScopesAsync(frameId, depth, maxChildren, ct);

    public Task<EvaluateResult> EvaluateAsync(string expression, int? frameId, CancellationToken ct)
        => _inspector.EvaluateAsync(expression, frameId, ct);

    public Task<EvaluateResult> SetExpressionAsync(
        string expression, string value, int? frameId, CancellationToken ct)
        => _inspector.SetExpressionAsync(expression, value, frameId, ct);

    public Task<ExceptionAutopsy> AutopsyAsync(int threadId, int topFrameCount, CancellationToken ct)
        => _inspector.AutopsyAsync(threadId, topFrameCount, ct);

    // ---- session lifecycle ----------------------------------------------------------

    public static async Task<DebugSession> LaunchAsync(
        string netcoredbgPath, string program, string[]? args, string? cwd, bool stopAtEntry,
        CancellationToken ct)
    {
        var session = StartAdapter(netcoredbgPath);
        try
        {
            await session.HandshakeAsync(
                isLaunch: true,
                startArgs: BuildLaunchArgs(program, args, cwd, stopAtEntry), ct).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<DebugSession> AttachAsync(
        string netcoredbgPath, int processId, CancellationToken ct)
    {
        var session = StartAdapter(netcoredbgPath);
        try
        {
            await session.HandshakeAsync(
                isLaunch: false,
                startArgs: new Dictionary<string, object?> { ["processId"] = processId }, ct)
                .ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static DebugSession StartAdapter(string netcoredbgPath)
    {
        var process = NetcoredbgProcess.Start(netcoredbgPath);
        var client = new DapClient(process.Input, process.Output);
        client.Start();
        return new DebugSession(process, client);
    }

    private static Dictionary<string, object?> BuildLaunchArgs(
        string program, string[]? args, string? cwd, bool stopAtEntry)
    {
        var d = new Dictionary<string, object?>
        {
            ["program"] = program,
            ["stopAtEntry"] = stopAtEntry,
            ["justMyCode"] = true,
        };
        if (args is { Length: > 0 }) d["args"] = args;
        if (!string.IsNullOrEmpty(cwd)) d["cwd"] = cwd;
        return d;
    }

    private async Task HandshakeAsync(
        bool isLaunch, Dictionary<string, object?> startArgs, CancellationToken ct)
    {
        var initialize = await _client.SendRequestAsync("initialize", new
        {
            clientID = "aspnetcore-debugger-mcp",
            adapterID = "coreclr",
            linesStartAt1 = true,
            columnsStartAt1 = true,
            pathFormat = "path",
        }, ct).ConfigureAwait(false);
        if (!initialize.Success)
            throw new DebugException($"initialize failed: {initialize.Message ?? "unknown"}");

        var startTask = _client.SendRequestAsync(isLaunch ? "launch" : "attach", startArgs, ct);

        await _initializedTcs.Task.WaitAsync(ct).ConfigureAwait(false);
        _stateMachine.Transition(SessionState.Configuring);

        var configDone = await _client.SendRequestAsync("configurationDone", null, ct).ConfigureAwait(false);
        if (!configDone.Success)
            throw new DebugException($"configurationDone failed: {configDone.Message ?? "unknown"}");

        var start = await startTask.ConfigureAwait(false);
        if (!start.Success)
        {
            var op = isLaunch ? "launch" : "attach";
            throw new DebugException($"{op} failed: {start.Message ?? "unknown"}");
        }

        _stateMachine.Transition(SessionState.Running);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        try
        {
            await _client.SendRequestAsync("disconnect", new { terminateDebuggee = true }, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // adapter may already be down; best-effort
        }
        _stateMachine.OnTerminated();
        _stopWaiter.Terminate();
    }

    // ---- execution control ----------------------------------------------------------

    public async Task ContinueAsync(int? threadId, CancellationToken ct)
    {
        var tid = ResolveThreadId(threadId);
        _stopWaiter.Reset();
        var resp = await _client.SendRequestAsync("continue",
            new Dictionary<string, object?> { ["threadId"] = tid }, ct).ConfigureAwait(false);
        if (!resp.Success)
            throw new DebugException($"continue failed: {resp.Message ?? "unknown"}");
    }

    public async Task PauseAsync(int? threadId, CancellationToken ct)
    {
        var tid = ResolveThreadId(threadId);
        var resp = await _client.SendRequestAsync("pause",
            new Dictionary<string, object?> { ["threadId"] = tid }, ct).ConfigureAwait(false);
        if (!resp.Success)
            throw new DebugException($"pause failed: {resp.Message ?? "unknown"}");
    }

    public async Task StepAsync(string kind, int? threadId, CancellationToken ct)
    {
        var dapCommand = kind switch
        {
            "in"   => "stepIn",
            "over" => "next",
            "out"  => "stepOut",
            _ => throw new DebugException($"Unknown step kind '{kind}'. Use one of: in, over, out."),
        };
        var tid = ResolveThreadId(threadId);
        _stopWaiter.Reset();
        var resp = await _client.SendRequestAsync(dapCommand,
            new Dictionary<string, object?> { ["threadId"] = tid }, ct).ConfigureAwait(false);
        if (!resp.Success)
            throw new DebugException($"{dapCommand} failed: {resp.Message ?? "unknown"}");
    }

    public async Task<StopInfo> WaitForStopAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        return await _stopWaiter.WaitAsync(linked.Token).ConfigureAwait(false);
    }

    private int ResolveThreadId(int? threadId)
    {
        if (threadId is int id) return id;
        var stop = LastStop;
        if (stop?.ThreadId is int known) return known;
        throw new DebugException(
            "No current thread known. Pass threadId explicitly, or hit a breakpoint first.");
    }

    // ---- breakpoints ----------------------------------------------------------------

    public async Task<LineBreakpoint> AddLineBreakpointAsync(
        string sourcePath, int line, string? condition, string? hitCondition, string? logMessage,
        CancellationToken ct)
    {
        var bp = _breakpoints.AddLine(sourcePath, line, condition, hitCondition, logMessage);
        await SyncSourceAsync(sourcePath, ct).ConfigureAwait(false);
        return _breakpoints.ForSource(sourcePath).First(b => b.Id == bp.Id);
    }

    public async Task<FunctionBreakpoint> AddFunctionBreakpointAsync(
        string functionName, string? condition, string? hitCondition, CancellationToken ct)
    {
        var bp = _breakpoints.AddFunction(functionName, condition, hitCondition);
        await SyncFunctionsAsync(ct).ConfigureAwait(false);
        return _breakpoints.AllFunction().First(f => f.Id == bp.Id);
    }

    public async Task<bool> RemoveBreakpointAsync(string id, CancellationToken ct)
    {
        var sourcePath = _breakpoints.GetSourcePathOf(id);
        var kind = _breakpoints.Remove(id);
        switch (kind)
        {
            case BreakpointKind.Line when sourcePath is not null:
                await SyncSourceAsync(sourcePath, ct).ConfigureAwait(false);
                return true;
            case BreakpointKind.Function:
                await SyncFunctionsAsync(ct).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    public async Task SetExceptionFiltersAsync(IEnumerable<string> filters, CancellationToken ct)
    {
        _breakpoints.SetExceptionFilters(filters);
        var current = _breakpoints.Snapshot().ExceptionFilters.ToArray();
        var resp = await _client.SendRequestAsync("setExceptionBreakpoints",
            new { filters = current }, ct).ConfigureAwait(false);
        if (!resp.Success)
            throw new DebugException($"setExceptionBreakpoints failed: {resp.Message ?? "unknown"}");
    }

    private async Task SyncSourceAsync(string sourcePath, CancellationToken ct)
    {
        var bps = _breakpoints.ForSource(sourcePath);
        var args = new
        {
            source = new { path = sourcePath, name = Path.GetFileName(sourcePath) },
            breakpoints = bps.Select(b => new
            {
                line = b.Line,
                condition = b.Condition,
                hitCondition = b.HitCondition,
                logMessage = b.LogMessage,
            }).ToArray(),
            lines = bps.Select(b => b.Line).ToArray(),
        };
        var resp = await _client.SendRequestAsync("setBreakpoints", args, ct).ConfigureAwait(false);
        if (!resp.Success)
            throw new DebugException($"setBreakpoints failed: {resp.Message ?? "unknown"}");

        ApplySetBreakpointsResponse(resp, bps, isLine: true);
    }

    private async Task SyncFunctionsAsync(CancellationToken ct)
    {
        var fns = _breakpoints.AllFunction();
        var args = new
        {
            breakpoints = fns.Select(f => new
            {
                name = f.FunctionName,
                condition = f.Condition,
                hitCondition = f.HitCondition,
            }).ToArray(),
        };
        var resp = await _client.SendRequestAsync("setFunctionBreakpoints", args, ct).ConfigureAwait(false);
        if (!resp.Success)
            throw new DebugException($"setFunctionBreakpoints failed: {resp.Message ?? "unknown"}");

        ApplySetBreakpointsResponse(resp, fns, isLine: false);
    }

    private void ApplySetBreakpointsResponse<T>(DapMessage resp, IReadOnlyList<T> registryBps, bool isLine)
    {
        if (resp.Body is not { } body) return;
        if (!body.TryGetProperty("breakpoints", out var arr) || arr.ValueKind != JsonValueKind.Array) return;

        int i = 0;
        foreach (var elem in arr.EnumerateArray())
        {
            if (i >= registryBps.Count) break;
            var verified = elem.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
            int? adapterId =
                elem.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                    ? idEl.GetInt32() : null;

            if (isLine && registryBps[i] is LineBreakpoint lb)
                _breakpoints.UpdateLine(lb.Id, verified, adapterId);
            else if (!isLine && registryBps[i] is FunctionBreakpoint fb)
                _breakpoints.UpdateFunction(fb.Id, verified, adapterId);

            i++;
        }
    }

    // ---- DAP event handling ---------------------------------------------------------

    private void OnDapEvent(DapMessage e)
    {
        switch (e.Event)
        {
            case "initialized":
                _initializedTcs.TrySetResult();
                break;

            case "stopped":
                HandleStopped(e);
                break;

            case "continued":
                _stateMachine.OnContinued();
                break;

            case "process":
                if (e.Body is { } p
                    && p.TryGetProperty("systemProcessId", out var pid)
                    && pid.ValueKind == JsonValueKind.Number)
                {
                    lock (_gate) _processId = pid.GetInt32();
                }
                break;

            case "breakpoint":
                HandleBreakpointEvent(e);
                break;

            case "terminated":
            case "exited":
                _stateMachine.OnTerminated();
                _stopWaiter.Terminate();
                break;
        }
    }

    private void HandleStopped(DapMessage e)
    {
        var reason = "unknown";
        int? threadId = null;
        string? description = null;
        if (e.Body is { } body)
        {
            if (body.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                reason = r.GetString() ?? reason;
            if (body.TryGetProperty("threadId", out var t) && t.ValueKind == JsonValueKind.Number)
                threadId = t.GetInt32();
            if (body.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                description = d.GetString();
        }
        var info = new StopInfo(reason, threadId, description);
        lock (_gate) _lastStop = info;
        _stateMachine.OnStopped();
        _stopWaiter.SetStop(info);
    }

    private void HandleBreakpointEvent(DapMessage e)
    {
        if (e.Body is not { } body) return;
        if (!body.TryGetProperty("breakpoint", out var bp)) return;
        if (!bp.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) return;

        var adapterId = idEl.GetInt32();
        var verified = bp.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
        _breakpoints.UpdateLineByAdapterId(adapterId, verified);
    }

    public async ValueTask DisposeAsync()
    {
        _client.EventReceived -= OnDapEvent;
        _stopWaiter.Terminate();
        await _client.DisposeAsync().ConfigureAwait(false);
        await _process.DisposeAsync().ConfigureAwait(false);
    }
}
