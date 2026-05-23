using System.ComponentModel;
using AspNetCoreDebuggerMcp.Debugging;
using ModelContextProtocol.Server;

namespace AspNetCoreDebuggerMcp.Tools;

[McpServerToolType]
public sealed class BreakpointTools
{
    private readonly DebugSessionManager _manager;

    public BreakpointTools(DebugSessionManager manager) => _manager = manager;

    [McpServerTool(Name = "breakpoint_set")]
    [Description("Set a line breakpoint in a source file. Supports conditional, hit-count, and logpoint (logMessage) breakpoints.")]
    public async Task<string> SetLineAsync(
        [Description("Absolute path to the source file.")] string sourcePath,
        [Description("Line number (1-based) to break on.")] int line,
        [Description("Optional expression — break only when this evaluates to true.")] string? condition = null,
        [Description("Optional hit count expression (e.g. \">5\") — break only on the Nth+ hit.")] string? hitCondition = null,
        [Description("If set, makes this a logpoint (tracepoint): the message is logged and execution continues without pausing. Use {expr} interpolation.")] string? logMessage = null,
        CancellationToken ct = default)
    {
        try
        {
            var bp = await _manager.AddLineBreakpointAsync(
                sourcePath, line, condition, hitCondition, logMessage, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, breakpoint = bp });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "breakpoint_set_function")]
    [Description("Set a breakpoint on a function by symbol name (e.g. \"Namespace.Class.Method\").")]
    public async Task<string> SetFunctionAsync(
        [Description("Fully-qualified function name to break on.")] string functionName,
        [Description("Optional condition expression.")] string? condition = null,
        [Description("Optional hit count expression.")] string? hitCondition = null,
        CancellationToken ct = default)
    {
        try
        {
            var bp = await _manager.AddFunctionBreakpointAsync(functionName, condition, hitCondition, ct)
                .ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, breakpoint = bp });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "breakpoint_remove")]
    [Description("Remove a breakpoint by its id (line or function).")]
    public async Task<string> RemoveAsync(
        [Description("Breakpoint id returned from breakpoint_set or breakpoint_set_function.")] string id,
        CancellationToken ct = default)
    {
        try
        {
            var removed = await _manager.RemoveBreakpointAsync(id, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, removed });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "breakpoint_list")]
    [Description("List all currently set breakpoints (line, function, and exception filters).")]
    public string List()
    {
        try
        {
            var snap = _manager.ListBreakpoints();
            return ToolResults.Serialize(new
            {
                success = true,
                line = snap.Line,
                function = snap.Function,
                data = snap.Data,
                exceptionFilters = snap.ExceptionFilters,
            });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "breakpoint_set_exception")]
    [Description("Set the active exception breakpoint filters. Pass [] to clear. Common netcoredbg filters: \"all\", \"user-unhandled\".")]
    public async Task<string> SetExceptionAsync(
        [Description("Filter names. Empty array clears exception breakpoints.")] string[] filters,
        CancellationToken ct = default)
    {
        try
        {
            await _manager.SetExceptionFiltersAsync(filters ?? Array.Empty<string>(), ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, exceptionFilters = filters });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "breakpoint_set_data")]
    [Description("Set a data/watch breakpoint: break when a specific variable changes (or is read). Requires a variablesReference + name from variables_get. May not be supported by all adapters.")]
    public async Task<string> SetDataAsync(
        [Description("variablesReference of the container holding the variable (from variables_get).")] int variablesReference,
        [Description("Name of the variable to watch.")] string name,
        [Description("Access type: \"write\" (default), \"read\", or \"readWrite\".")] string accessType = "write",
        CancellationToken ct = default)
    {
        try
        {
            var bp = await _manager.AddDataBreakpointAsync(variablesReference, name, accessType, ct)
                .ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true, breakpoint = bp });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }
}
