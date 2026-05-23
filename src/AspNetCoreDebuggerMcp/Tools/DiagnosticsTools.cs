using System.ComponentModel;
using AspNetCoreDebuggerMcp.Debugging;
using AspNetCoreDebuggerMcp.Diagnostics;
using ModelContextProtocol.Server;

namespace AspNetCoreDebuggerMcp.Tools;

[McpServerToolType]
public sealed class DiagnosticsTools
{
    private const int DefaultTopFramesPerThread = 10;

    private readonly DebugSessionManager _manager;

    public DiagnosticsTools(DebugSessionManager manager) => _manager = manager;

    [McpServerTool(Name = "hang_analyze")]
    [Description("Diagnose why an application appears stuck. Auto-pauses if running, lists all threads, fetches the top frames of each, and classifies each thread's blocking pattern (Monitor / WaitHandle / Semaphore / Task / Thread.Join / Thread.Sleep / async await). Leaves the session paused so you can inspect further.")]
    public async Task<string> HangAnalyzeAsync(
        [Description("Top frames per thread to fetch. Default 10.")] int? topFramesPerThread = null,
        CancellationToken ct = default)
    {
        try
        {
            var analysis = await _manager.HangAnalyzeAsync(
                topFramesPerThread ?? DefaultTopFramesPerThread, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new
            {
                success = true,
                blockedCount = analysis.BlockedCount,
                cycleDetectionAvailable = analysis.CycleDetectionAvailable,
                notes = analysis.Notes,
                threads = analysis.Threads,
            });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "trace_start")]
    [Description("Begin tracing a set of methods. Each named method gets a server-side trace breakpoint that captures the call (top stack + locals) and auto-continues — the request flows through at near-normal speed and your debug state is unaffected. If includeExceptions=true, unhandled exceptions are also captured. Use trace_get to read the captured events and trace_stop to remove the trace. One trace active at a time.")]
    public async Task<string> TraceStartAsync(
        [Description("Function names to trace (e.g. \"Namespace.Class.Method\"). Same format as breakpoint_set_function.")] string[] methods,
        [Description("Capture top stack at each hit. Default true.")] bool captureStack = true,
        [Description("Capture top-frame locals at each hit. Default true.")] bool captureLocals = true,
        [Description("Also capture unhandled exceptions during the trace. Default true.")] bool includeExceptions = true,
        [Description("Maximum stack frames per captured event. Default 10.")] int? maxFramesPerEvent = null,
        [Description("Maximum locals per captured event. Default 10.")] int? maxLocalsPerFrame = null,
        CancellationToken ct = default)
    {
        try
        {
            if (methods is null || methods.Length == 0)
                throw new ArgumentException("methods must contain at least one function name.");
            var cfg = await _manager.TraceStartAsync(methods, captureStack, captureLocals, includeExceptions,
                maxFramesPerEvent ?? 10, maxLocalsPerFrame ?? 10, ct).ConfigureAwait(false);
            return ToolResults.Serialize(new
            {
                success = true,
                tracing = cfg.Methods,
                includeExceptions = cfg.IncludeExceptions,
                note = "Trace is active. Run your request, then call trace_get to read events.",
            });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "trace_get")]
    [Description("Read events captured since trace_start, plus a pre-rendered ASCII timeline with → for method entries and ⚠ for exceptions. Does NOT clear the buffer — call trace_stop when done.")]
    public string TraceGet(
        [Description("Return only the most recent N events.")] int? maxEvents = null)
    {
        try
        {
            var events = _manager.TraceGet(maxEvents);
            return ToolResults.Serialize(new
            {
                success = true,
                eventCount = events.Count,
                tree = TraceRenderer.Render(events),
                events,
            });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "trace_stop")]
    [Description("Stop the active trace and remove its breakpoints. Captured events are discarded — call trace_get first if you want to keep them.")]
    public async Task<string> TraceStopAsync(CancellationToken ct = default)
    {
        try
        {
            await _manager.TraceStopAsync(ct).ConfigureAwait(false);
            return ToolResults.Serialize(new { success = true });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }

    [McpServerTool(Name = "process_read_output")]
    [Description("Drain buffered output (stdout/stderr) from the debuggee since the previous call. Returns the lines collected and removes them from the buffer.")]
    public string ProcessReadOutput(
        [Description("Filter by category: \"stdout\", \"stderr\", \"console\", or omit for all.")] string? category = null,
        [Description("Maximum lines to drain in this call.")] int? maxLines = null)
    {
        try
        {
            var lines = _manager.DrainOutput(category, maxLines);
            return ToolResults.Serialize(new { success = true, lines });
        }
        catch (Exception ex) { return ToolResults.Err(ex); }
    }
}
