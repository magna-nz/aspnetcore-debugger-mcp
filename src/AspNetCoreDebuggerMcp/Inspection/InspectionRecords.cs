namespace AspNetCoreDebuggerMcp.Inspection;

public sealed record ThreadInfo(int Id, string Name);

public sealed record StackFrame(
    int Id,
    string Name,
    string? SourcePath,
    int? Line,
    int? Column);

public sealed record VariableInfo(
    string Name,
    string Value,
    string? Type,
    int VariablesReference,
    IReadOnlyList<VariableInfo>? Children,
    int? TotalChildren,
    bool Truncated);

public sealed record ScopeWithVariables(
    string Name,
    int VariablesReference,
    bool Expensive,
    IReadOnlyList<VariableInfo> Variables);

public sealed record EvaluateResult(
    string Result,
    string? Type,
    int VariablesReference);

public sealed record SourceSnippet(
    string Path,
    int StartLine,
    int EndLine,
    int HighlightLine,
    string Text);

public sealed record ExceptionAutopsy(
    string ThreadId,
    string? ExceptionId,
    string? Description,
    string? BreakMode,
    IReadOnlyList<ExceptionLayer> Layers,
    IReadOnlyList<StackFrame> StackFrames,
    IReadOnlyList<ScopeWithVariables>? TopFrameLocals,
    SourceSnippet? TopFrameSnippet);

public sealed record ExceptionLayer(
    string? TypeName,
    string? Message,
    string? Source);

public sealed record FrameWithLocals(
    StackFrame Frame,
    IReadOnlyList<ScopeWithVariables> Scopes);

public sealed record StackExplore(
    IReadOnlyList<FrameWithLocals> Frames,
    string Tree);
