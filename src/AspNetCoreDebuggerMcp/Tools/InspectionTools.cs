using System.ComponentModel;
using AspNetCoreDebuggerMcp.Debugging;
using ModelContextProtocol.Server;

namespace AspNetCoreDebuggerMcp.Tools;

[McpServerToolType]
public sealed class InspectionTools
{
    private const int DefaultDepth = 1;
    private const int DefaultMaxChildren = 50;
    private const int DefaultAutopsyFrameCount = 20;
    private const int DefaultMaxRecentOutputLines = 50;

    private readonly DebugSessionManager _manager;

    public InspectionTools(DebugSessionManager manager) => _manager = manager;

    [McpServerTool(Name = "threads_list")]
    [Description("List all threads in the debuggee.")]
    public async Task<string> ListThreadsAsync(CancellationToken ct = default)
    {
        try
        {
            var threads = await _manager.ListThreadsAsync(ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, threads });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "stacktrace_get")]
    [Description("Get the call stack of a thread. By default async state-machine frames are flattened back to their original method names (e.g. UserService.<GetAsync>d__3.MoveNext → UserService.GetAsync) and BCL async infrastructure frames are hidden. Pass raw=true for the unmodified DAP frames.")]
    public async Task<string> StackTraceAsync(
        [Description("Thread id. Defaults to the last-stopped thread.")] int? threadId = null,
        [Description("Skip this many top frames (0 = include the topmost).")] int? startFrame = null,
        [Description("Maximum frames to return.")] int? levels = null,
        [Description("If true, return raw DAP frames (no async flattening, no infrastructure filtering).")] bool raw = false,
        CancellationToken ct = default)
    {
        try
        {
            var frames = await _manager.GetStackTraceAsync(threadId, startFrame, levels, raw, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, frames });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "variables_get")]
    [Description("Get variables for a stack frame. Defaults to the topmost frame of the last-stopped thread. Recursively expands compound values up to `depth` levels and truncates each level at `maxChildren`.")]
    public async Task<string> VariablesAsync(
        [Description("Frame id from stacktrace_get. Defaults to the topmost frame of the last-stopped thread.")] int? frameId = null,
        [Description("Recursive expansion depth. 1 = just the top-level variables (default). 2 = expand one level into compound types. Higher = deeper.")] int? depth = null,
        [Description("Maximum children to return at each level. Default 50.")] int? maxChildren = null,
        CancellationToken ct = default)
    {
        try
        {
            var scopes = await _manager.GetScopesAsync(
                frameId, depth ?? DefaultDepth, maxChildren ?? DefaultMaxChildren, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, scopes });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "evaluate")]
    [Description("Evaluate a C# expression in the context of a stack frame. Returns the result as a string plus a variablesReference if the result is a compound value.")]
    public async Task<string> EvaluateAsync(
        [Description("Expression to evaluate (e.g. \"user.Id\", \"items.Count\").")] string expression,
        [Description("Frame id from stacktrace_get. Defaults to the global (no-frame) context.")] int? frameId = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _manager.EvaluateAsync(expression, frameId, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, result });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "variables_set")]
    [Description("Set the value of a variable or any lvalue expression (e.g. \"userId\" or \"user.Name\"). The agent can use this to test fixes by mutating state mid-run.")]
    public async Task<string> SetVariableAsync(
        [Description("The lvalue expression to assign to (e.g. \"userId\", \"user.Name\").")] string expression,
        [Description("The new value as a C# expression (e.g. \"42\", \"\\\"hello\\\"\").")] string value,
        [Description("Frame id from stacktrace_get. Defaults to the global (no-frame) context.")] int? frameId = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _manager.SetExpressionAsync(expression, value, frameId, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, result });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "stack_explore")]
    [Description("In one call: full stack + locals at every frame + a pre-rendered ASCII tree showing caller → callee with arrows. Use this instead of stacktrace_get + variables_get per frame when you want to see the whole picture at once.")]
    public async Task<string> StackExploreAsync(
        [Description("Thread id. Defaults to the last-stopped thread.")] int? threadId = null,
        [Description("Maximum frames to include. Default 10.")] int? maxFrames = null,
        [Description("Maximum locals per frame. Default 10.")] int? maxLocalsPerFrame = null,
        CancellationToken ct = default)
    {
        try
        {
            var explore = await _manager.StackExploreAsync(
                threadId, maxFrames ?? 10, maxLocalsPerFrame ?? 10, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new
            {
                success = true,
                tree = explore.Tree,
                frames = explore.Frames,
            });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "exception_autopsy")]
    [Description("Full exception context in one call: exception type + inner-exception chain + top stack frames + top frame's locals + source snippet around the throw + a peek of recent debuggee stdout/stderr (non-destructive — process_read_output still drains the full buffer). Call this when state.lastStop.reason == \"exception\".")]
    public async Task<string> AutopsyAsync(
        [Description("Thread id. Defaults to the last-stopped thread.")] int? threadId = null,
        [Description("How many top stack frames to include. Default 20.")] int? frameCount = null,
        [Description("Cap on recent debuggee output lines included with the autopsy (peeked, not drained). Default 50. Pass 0 to omit output.")] int? maxRecentOutputLines = null,
        CancellationToken ct = default)
    {
        try
        {
            var autopsy = await _manager.AutopsyAsync(
                threadId, frameCount ?? DefaultAutopsyFrameCount, ct).ConfigureAwait(false);
            var outputCap = Math.Max(0, maxRecentOutputLines ?? DefaultMaxRecentOutputLines);
            var recentOutput = outputCap > 0 ? _manager.PeekRecentOutput(outputCap) : null;
            return ToolResults.Serialize(new { success = true, autopsy, recentOutput });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }
}
