namespace AspNetCoreDebuggerMcp.Debugging;

public sealed record StopInfo(string Reason, int? ThreadId, string? Description);

public sealed record SessionSnapshot(
    string State,
    int? ProcessId,
    StopInfo? LastStop)
{
    public static SessionSnapshot None { get; } = new("None", null, null);
}
