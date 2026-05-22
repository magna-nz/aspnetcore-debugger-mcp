using System.Text.Json;
using AspNetCoreDebuggerMcp.Dap;

namespace AspNetCoreDebuggerMcp.Debugging;

/// One active debug session: owns the netcoredbg process and the DAP client,
/// runs the launch/attach handshake, and translates DAP events into state transitions.
internal sealed class DebugSession : IAsyncDisposable
{
    private readonly NetcoredbgProcess _process;
    private readonly DapClient _client;
    private readonly SessionStateMachine _stateMachine = new();
    private readonly TaskCompletionSource _initializedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private int? _processId;
    private StopInfo? _lastStop;

    public SessionState State => _stateMachine.State;
    public int? ProcessId { get { lock (_gate) return _processId; } }
    public StopInfo? LastStop { get { lock (_gate) return _lastStop; } }

    public SessionSnapshot Snapshot() => new(State.ToString(), ProcessId, LastStop);

    private DebugSession(NetcoredbgProcess process, DapClient client)
    {
        _process = process;
        _client = client;
        _client.EventReceived += OnDapEvent;
    }

    public static async Task<DebugSession> LaunchAsync(
        string netcoredbgPath,
        string program,
        string[]? args,
        string? cwd,
        bool stopAtEntry,
        CancellationToken ct)
    {
        var session = StartAdapter(netcoredbgPath);
        try
        {
            await session.HandshakeAsync(
                isLaunch: true,
                startArgs: BuildLaunchArgs(program, args, cwd, stopAtEntry),
                ct).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<DebugSession> AttachAsync(
        string netcoredbgPath,
        int processId,
        CancellationToken ct)
    {
        var session = StartAdapter(netcoredbgPath);
        try
        {
            await session.HandshakeAsync(
                isLaunch: false,
                startArgs: new Dictionary<string, object?> { ["processId"] = processId },
                ct).ConfigureAwait(false);
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

        // Send launch/attach now; its response may not arrive until after configurationDone.
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
            // Adapter may already be down — best-effort.
        }
        _stateMachine.OnTerminated();
    }

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
                if (e.Body is { } body
                    && body.TryGetProperty("systemProcessId", out var pid)
                    && pid.ValueKind == JsonValueKind.Number)
                {
                    lock (_gate) _processId = pid.GetInt32();
                }
                break;
            case "terminated":
            case "exited":
                _stateMachine.OnTerminated();
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
        lock (_gate) _lastStop = new StopInfo(reason, threadId, description);
        _stateMachine.OnStopped();
    }

    public async ValueTask DisposeAsync()
    {
        _client.EventReceived -= OnDapEvent;
        await _client.DisposeAsync().ConfigureAwait(false);
        await _process.DisposeAsync().ConfigureAwait(false);
    }
}
