using System.Text;

namespace AspNetCoreDebuggerMcp.Inspection;

/// Pre-renders a stack + per-frame locals as an ASCII tree. Reads top-to-bottom as
/// "caller calls callee" (outermost first, paused frame last with a marker), so the
/// output matches how people describe a call chain: Controller → Service → Repository.
public static class StackTreeRenderer
{
    private const int MaxValueLength = 40;

    public static string Render(IReadOnlyList<FrameWithLocals> frames)
    {
        if (frames.Count == 0) return "(no frames)";

        var sb = new StringBuilder();
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            var f = frames[i];
            var location = (f.Frame.SourcePath, f.Frame.Line) switch
            {
                (string p, int l) => $"   [{Path.GetFileName(p)}:{l}]",
                _ => "",
            };
            var marker = i == 0 ? "  ◄ paused here" : "";
            sb.Append(f.Frame.Name).Append(location).Append(marker).AppendLine();

            var localsLine = FormatLocals(f.Scopes);
            if (localsLine.Length > 0)
                sb.Append("  ").AppendLine(localsLine);

            if (i > 0)
            {
                sb.AppendLine("     │");
                sb.AppendLine("     ▼");
            }
        }
        return sb.ToString();
    }

    private static string FormatLocals(IReadOnlyList<ScopeWithVariables> scopes)
    {
        var scope = scopes.FirstOrDefault(s => string.Equals(s.Name, "Locals", StringComparison.OrdinalIgnoreCase))
                    ?? scopes.FirstOrDefault();
        if (scope is null || scope.Variables.Count == 0) return "";

        return string.Join(", ", scope.Variables.Select(v =>
        {
            var val = v.Value.Length > MaxValueLength
                ? v.Value[..MaxValueLength] + "…"
                : v.Value;
            return $"{v.Name} = {val}";
        }));
    }
}
