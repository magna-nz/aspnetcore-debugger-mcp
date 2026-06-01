using AspNetCoreDebuggerMcp.Diagnostics;

namespace AspNetCoreDebuggerMcp.Debugging;

public sealed record StopInfo(string Reason, int? ThreadId, string? Description);

public sealed record SessionSnapshot(
    string State,
    int? ProcessId,
    StopInfo? LastStop,
    OutputBufferStats? OutputBuffer = null,
    TraceBufferStats? TraceBuffer = null)
{
    public static SessionSnapshot None { get; } = new("None", null, null);
}
