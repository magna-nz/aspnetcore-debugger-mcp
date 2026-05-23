namespace AspNetCoreDebuggerMcp.Diagnostics;

public sealed record OutputLine(string Category, string Output, DateTimeOffset Timestamp);
