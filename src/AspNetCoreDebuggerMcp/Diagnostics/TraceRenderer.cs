using System.Text;

namespace AspNetCoreDebuggerMcp.Diagnostics;

/// Pure ASCII renderer for a trace timeline.
/// Method entries get `→`, exceptions get `⚠`. Indentation grows with stack depth so the
/// reader can see the call chain at a glance.
public static class TraceRenderer
{
    private const int MaxValueLength = 40;

    public static string Render(IReadOnlyList<TraceEvent> events)
    {
        if (events.Count == 0) return "(no trace events)";

        // Min stack depth across all events — we indent relative to this so the outermost
        // captured frame sits at column 0 regardless of how deep into Kestrel we actually are.
        int minDepth = int.MaxValue;
        foreach (var e in events)
        {
            var d = e.Stack?.Count ?? 1;
            if (d < minDepth) minDepth = d;
        }
        if (minDepth == int.MaxValue) minDepth = 1;

        var sb = new StringBuilder();
        foreach (var e in events)
        {
            var depth = (e.Stack?.Count ?? minDepth) - minDepth;
            var pad = new string(' ', Math.Max(0, depth) * 2);
            var t = $"[+{e.ElapsedMs,5}ms]";

            if (e.Kind == TraceEventKind.Enter)
            {
                sb.Append(t).Append(' ').Append(pad).Append("→ ").Append(e.Method);
                var args = FormatLocals(e.Locals);
                if (args.Length > 0) sb.Append("  ").Append(args);
                sb.AppendLine();
            }
            else
            {
                sb.Append(t).Append(' ').Append(pad).Append("⚠ ")
                  .Append(e.ExceptionType ?? "Exception");
                if (!string.IsNullOrEmpty(e.ExceptionMessage))
                    sb.Append(": ").Append(e.ExceptionMessage);
                sb.AppendLine();
                if (e.Stack is { Count: > 0 } stack)
                {
                    foreach (var f in stack.Take(5))
                    {
                        sb.Append(t).Append(' ').Append(pad).Append("    at ").Append(f.Name);
                        if (f.SourcePath is not null && f.Line is int line)
                            sb.Append(" [").Append(Path.GetFileName(f.SourcePath)).Append(':').Append(line).Append(']');
                        sb.AppendLine();
                    }
                }
            }
        }
        return sb.ToString();
    }

    private static string FormatLocals(IReadOnlyList<Inspection.VariableInfo>? locals)
    {
        if (locals is null || locals.Count == 0) return "";
        return string.Join(", ", locals.Select(v =>
        {
            var val = v.Value.Length > MaxValueLength ? v.Value[..MaxValueLength] + "…" : v.Value;
            return $"{v.Name}={val}";
        }));
    }
}
