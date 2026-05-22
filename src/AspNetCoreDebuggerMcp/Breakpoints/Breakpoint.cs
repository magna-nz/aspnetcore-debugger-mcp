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

public sealed record BreakpointsSnapshot(
    IReadOnlyList<LineBreakpoint> Line,
    IReadOnlyList<FunctionBreakpoint> Function,
    IReadOnlyList<string> ExceptionFilters);
