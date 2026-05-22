using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using AspNetCoreDebuggerMcp.Debugging;
using ModelContextProtocol.Server;

namespace AspNetCoreDebuggerMcp.Tools;

[McpServerToolType]
public sealed class SessionTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly DebugSessionManager _manager;

    public SessionTools(DebugSessionManager manager) => _manager = manager;

    [McpServerTool(Name = "debug_launch")]
    [Description("Launch a .NET program under the debugger. Returns the resulting session state.")]
    public async Task<string> DebugLaunchAsync(
        [Description("Path to the .NET program to debug (.dll or apphost executable).")] string program,
        [Description("Command-line arguments to pass to the program.")] string[]? args = null,
        [Description("Working directory for the program (defaults to the program's directory).")] string? cwd = null,
        [Description("If true, the program pauses at entry instead of running until the first breakpoint.")] bool stopAtEntry = false,
        CancellationToken ct = default)
    {
        try
        {
            var snap = await _manager.LaunchAsync(program, args, cwd, stopAtEntry, ct).ConfigureAwait(false);
            return Ok(snap);
        }
        catch (Exception ex) { return Err(ex); }
    }

    [McpServerTool(Name = "debug_attach")]
    [Description("Attach the debugger to an already-running .NET process by PID.")]
    public async Task<string> DebugAttachAsync(
        [Description("System process id (PID) of the .NET process to attach to.")] int processId,
        CancellationToken ct = default)
    {
        try
        {
            var snap = await _manager.AttachAsync(processId, ct).ConfigureAwait(false);
            return Ok(snap);
        }
        catch (Exception ex) { return Err(ex); }
    }

    [McpServerTool(Name = "debug_disconnect")]
    [Description("Disconnect the debugger and terminate the debuggee.")]
    public async Task<string> DebugDisconnectAsync(CancellationToken ct = default)
    {
        try
        {
            var snap = await _manager.DisconnectAsync(ct).ConfigureAwait(false);
            return Ok(snap);
        }
        catch (Exception ex) { return Err(ex); }
    }

    [McpServerTool(Name = "debug_state")]
    [Description("Return the current debug session state, including process id and last stop info.")]
    public string DebugState()
    {
        try { return Ok(_manager.GetState()); }
        catch (Exception ex) { return Err(ex); }
    }

    private static string Ok(SessionSnapshot snap) => JsonSerializer.Serialize(new
    {
        success = true,
        state = snap.State,
        processId = snap.ProcessId,
        lastStop = snap.LastStop,
    }, JsonOpts);

    private static string Err(Exception ex)
        => JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOpts);
}
