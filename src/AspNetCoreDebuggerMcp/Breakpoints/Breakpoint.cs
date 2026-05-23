namespace AspNetCoreDebuggerMcp.Breakpoints;

public sealed record LineBreakpoint(
    string Id,
    string SourcePath,
    int Line,
    string? Condition,
    string? HitCondition,
    string? LogMessage,
    bool Verified,
    int? AdapterId);

public sealed record FunctionBreakpoint(
    string Id,
    string FunctionName,
    string? Condition,
    string? HitCondition,
    bool Verified,
    int? AdapterId);

public sealed record DataBreakpoint(
    string Id,
    string DataId,          // DAP-issued opaque token from dataBreakpointInfo
    string Description,     // human-readable, from the probe
    string AccessType,      // "read" | "write" | "readWrite"
    bool Verified,
    int? AdapterId);

public sealed record BreakpointsSnapshot(
    IReadOnlyList<LineBreakpoint> Line,
    IReadOnlyList<FunctionBreakpoint> Function,
    IReadOnlyList<DataBreakpoint> Data,
    IReadOnlyList<string> ExceptionFilters);
