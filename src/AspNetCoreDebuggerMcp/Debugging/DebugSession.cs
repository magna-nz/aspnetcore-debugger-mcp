using System.Collections.Concurrent;
using System.Text.Json;
using AspNetCoreDebuggerMcp.Breakpoints;
using AspNetCoreDebuggerMcp.Dap;
using AspNetCoreDebuggerMcp.Diagnostics;
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
    private readonly ConcurrentQueue<OutputLine> _outputBuffer = new();
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
            case BreakpointKind.Data:
                await SyncDataBreakpointsAsync(ct).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    public async Task<DataBreakpoint> AddDataBreakpointAsync(
        int variablesReference, string name, string accessType, CancellationToken ct)
    {
        var probe = await _client.SendRequestAsync("dataBreakpointInfo",
            new { variablesReference, name }, ct).ConfigureAwait(false);
        if (!probe.Success)
            throw new DebugException($"dataBreakpointInfo failed: {probe.Message ?? "unknown"}");

        string? dataId = null;
        string? description = name;
        if (probe.Body is { } body)
        {
            if (body.TryGetProperty("dataId", out var didEl) && didEl.ValueKind == JsonValueKind.String)
                dataId = didEl.GetString();
            if (body.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                description = d.GetString();
        }
        if (string.IsNullOrEmpty(dataId))
            throw new DebugException(
                $"Cannot set a data breakpoint on '{name}': adapter returned no dataId" +
                (description != name ? $" ({description})" : ""));

        var bp = _breakpoints.AddData(dataId, description ?? name, accessType);
        await SyncDataBreakpointsAsync(ct).ConfigureAwait(false);
        return _breakpoints.AllData().First(b => b.Id == bp.Id);
    }

    private async Task SyncDataBreakpointsAsync(CancellationToken ct)
    {
        var data = _breakpoints.AllData();
        var args = new
        {
            breakpoints = data.Select(b => new
            {
                dataId = b.DataId,
                accessType = b.AccessType,
            }).ToArray(),
        };
        var resp = await _client.SendRequestAsync("setDataBreakpoints", args, ct).ConfigureAwait(false);
        if (!resp.Success)
            throw new DebugException($"setDataBreakpoints failed: {resp.Message ?? "unknown"}");

        if (resp.Body is { } body && body.TryGetProperty("breakpoints", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var elem in arr.EnumerateArray())
            {
                if (i >= data.Count) break;
                var verified = elem.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
                int? adapterId =
                    elem.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetInt32() : null;
                _breakpoints.UpdateData(data[i].Id, verified, adapterId);
                i++;
            }
        }
    }

    // ---- output buffer + hang analysis ----------------------------------------------

    public IReadOnlyList<OutputLine> DrainOutput(string? category = null, int? maxLines = null)
    {
        var collected = new List<OutputLine>();
        while (_outputBuffer.TryDequeue(out var line))
        {
            if (category is null || string.Equals(line.Category, category, StringComparison.OrdinalIgnoreCase))
                collected.Add(line);
            if (maxLines is int m && collected.Count >= m) break;
        }
        return collected;
    }

    public async Task<HangAnalysis> HangAnalyzeAsync(int topFramesPerThread, CancellationToken ct)
    {
        if (State == SessionState.Running)
        {
            // Pause any thread so we can inspect. DAP threads is callable while running.
            var liveThreads = await ListThreadsAsync(ct).ConfigureAwait(false);
            if (liveThreads.Count == 0)
                throw new DebugException("No threads available to pause for hang analysis.");
            _stopWaiter.Reset();
            try { await PauseAsync(liveThreads[0].Id, ct).ConfigureAwait(false); }
            catch (Exception ex) { throw new DebugException($"hang_analyze: pause failed: {ex.Message}", ex); }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(5));
                await _stopWaiter.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch { /* may not stop within timeout; analyze what we can */ }
        }

        var threads = await ListThreadsAsync(ct).ConfigureAwait(false);
        var infos = new List<ThreadHangInfo>(threads.Count);
        foreach (var t in threads)
        {
            IReadOnlyList<string> names = Array.Empty<string>();
            try
            {
                var frames = await GetStackTraceAsync(t.Id, 0, topFramesPerThread, ct).ConfigureAwait(false);
                names = frames.Select(f => f.Name).ToList();
            }
            catch { /* if a single thread fails, keep going */ }
            infos.Add(new ThreadHangInfo(t.Id, t.Name, ThreadAnalyzer.Classify(names), names));
        }
        return ThreadAnalyzer.Analyze(infos);
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

            case "output":
                HandleOutput(e);
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

    private void HandleOutput(DapMessage e)
    {
        if (e.Body is not { } body) return;
        var category = body.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? "stdout" : "stdout";
        var output = body.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String
            ? o.GetString() ?? "" : "";
        if (output.Length == 0) return;
        _outputBuffer.Enqueue(new OutputLine(category, output, DateTimeOffset.UtcNow));
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
