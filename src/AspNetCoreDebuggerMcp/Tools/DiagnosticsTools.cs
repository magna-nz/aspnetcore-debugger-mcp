using System.ComponentModel;
using AspNetCoreDebuggerMcp.Debugging;
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
