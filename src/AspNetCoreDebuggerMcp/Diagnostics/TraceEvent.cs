using AspNetCoreDebuggerMcp.Inspection;

namespace AspNetCoreDebuggerMcp.Diagnostics;

public enum TraceEventKind { Enter, Exception }

public sealed record TraceEvent(
    long ElapsedMs,
    TraceEventKind Kind,
    int ThreadId,
    string? Method,               // for Enter
    string? ExceptionType,        // for Exception
    string? ExceptionMessage,     // for Exception
    IReadOnlyList<VariableInfo>? Locals,
    IReadOnlyList<StackFrame>? Stack);
