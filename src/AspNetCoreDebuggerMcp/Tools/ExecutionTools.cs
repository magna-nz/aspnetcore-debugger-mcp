using System.ComponentModel;
using AspNetCoreDebuggerMcp.Debugging;
using ModelContextProtocol.Server;

namespace AspNetCoreDebuggerMcp.Tools;

[McpServerToolType]
public sealed class ExecutionTools
{
    private const int DefaultWaitTimeoutSeconds = 30;
    private const int DefaultMaxLocalsPerScope = 30;
    private const int DefaultMaxRecentOutputLines = 50;

    private readonly DebugSessionManager _manager;

    public ExecutionTools(DebugSessionManager manager) => _manager = manager;

    [McpServerTool(Name = "debug_continue")]
    [Description("Resume execution of the debuggee. Defaults to the last-stopped thread.")]
    public async Task<string> ContinueAsync(
        [Description("Thread id to continue. If omitted, uses the last thread that stopped.")] int? threadId = null,
        CancellationToken ct = default)
    {
        try
        {
            var snap = await _manager.ContinueAsync(threadId, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, state = snap.State, processId = snap.ProcessId });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "debug_pause")]
    [Description("Pause the debuggee. Requires a thread id; defaults to the last-stopped thread if known.")]
    public async Task<string> PauseAsync(
        [Description("Thread id to pause. If omitted, uses the last thread that stopped.")] int? threadId = null,
        CancellationToken ct = default)
    {
        try
        {
            var snap = await _manager.PauseAsync(threadId, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, state = snap.State });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "debug_step")]
    [Description("Single-step the debuggee. Kind: \"in\" (step into), \"over\" (step over), \"out\" (step out).")]
    public async Task<string> StepAsync(
        [Description("Step kind: \"in\", \"over\", or \"out\".")] string kind,
        [Description("Thread id to step. If omitted, uses the last thread that stopped.")] int? threadId = null,
        CancellationToken ct = default)
    {
        try
        {
            var snap = await _manager.StepAsync(kind, threadId, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, state = snap.State });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "breakpoint_wait")]
    [Description("Block until the debuggee hits a breakpoint, completes a step, or otherwise stops. Returns the stop info plus a full one-shot snapshot: the topmost stack frame, a source snippet around the stop, top-frame locals, and a peek of recent debuggee stdout/stderr (non-destructive — process_read_output still drains the full buffer). Designed so an agent in a step-inspect loop doesn't need separate inspect / read-output round trips.")]
    public async Task<string> WaitAsync(
        [Description("Maximum seconds to wait. Defaults to 30.")] int? timeoutSeconds = null,
        [Description("Cap on locals returned per scope on the top frame. Default 30. Pass 0 to omit locals entirely.")] int? maxLocalsPerScope = null,
        [Description("Cap on recent debuggee output lines included with the stop (peeked, not drained). Default 50. Pass 0 to omit output.")] int? maxRecentOutputLines = null,
        CancellationToken ct = default)
    {
        try
        {
            var timeout = TimeSpan.FromSeconds(timeoutSeconds ?? DefaultWaitTimeoutSeconds);
            var localsCap = Math.Max(0, maxLocalsPerScope ?? DefaultMaxLocalsPerScope);
            var outputCap = Math.Max(0, maxRecentOutputLines ?? DefaultMaxRecentOutputLines);
            var result = await _manager.WaitForStopAsync(timeout, localsCap, outputCap, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new
            {
                success = true,
                stop = result.Stop,
                state = result.Session.State,
                processId = result.Session.ProcessId,
                topFrame = result.TopFrame,
                snippet = result.Snippet,
                topFrameLocals = result.TopFrameLocals,
                recentOutput = result.RecentOutput,
            });
        }
        catch (OperationCanceledException)
        {
            return ToolResults.Serialize(new { success = false, error = "timeout", state = _manager.GetState().State });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }
}
