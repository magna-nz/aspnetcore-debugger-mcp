using System.Text;

namespace AspNetCoreDebuggerMcp.Diagnostics;

/// Pure ASCII renderer for a trace timeline.
/// Each line gets `→` for method entries (and `⚠` for exceptions), prefixed with `--` per
/// level of nesting. Depth is computed by counting how many OTHER captured methods appear in
/// this event's stack above its top frame — so a request that goes Controller → Service →
/// Repository renders as 0 → 1 → 2 dashes, regardless of how many Kestrel frames sit underneath.
public static class TraceRenderer
{
    private const int MaxValueLength = 40;

    public static string Render(IReadOnlyList<TraceEvent> events)
    {
        if (events.Count == 0) return "(no trace events)";

        // Build the set of method names we ourselves captured. Used to count traced ancestors
        // in each event's stack.
        var tracedMethods = events
            .Where(e => e.Kind == TraceEventKind.Enter && e.Method is not null)
            .Select(e => e.Method!)
            .ToHashSet(StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var e in events)
        {
            var depth = Depth(e, tracedMethods);
            var dashes = depth == 0 ? "" : new string('-', depth * 2);
            var t = $"[+{e.ElapsedMs,5}ms]";

            if (e.Kind == TraceEventKind.Enter)
            {
                sb.Append(t).Append(' ').Append(dashes).Append("→ ").Append(e.Method);
                var args = FormatLocals(e.Locals);
                if (args.Length > 0) sb.Append("  ").Append(args);
                sb.AppendLine();
            }
            else
            {
                sb.Append(t).Append(' ').Append(dashes).Append("⚠ ")
                  .Append(e.ExceptionType ?? "Exception");
                if (!string.IsNullOrEmpty(e.ExceptionMessage))
                    sb.Append(": ").Append(e.ExceptionMessage);
                sb.AppendLine();
                if (e.Stack is { Count: > 0 } stack)
                {
                    foreach (var f in stack.Take(5))
                    {
                        sb.Append(t).Append(' ').Append(dashes).Append("    at ").Append(f.Name);
                        if (f.SourcePath is not null && f.Line is int line)
                            sb.Append(" [").Append(Path.GetFileName(f.SourcePath)).Append(':').Append(line).Append(']');
                        sb.AppendLine();
                    }
                }
            }
        }
        return sb.ToString();
    }

    private static int Depth(TraceEvent e, HashSet<string> tracedMethods)
    {
        if (e.Stack is null || e.Stack.Count < 2) return 0;
        // Count traced ancestors anywhere above the top frame. Non-traced frames between two
        // traced frames (e.g. middleware between controller and service) don't break the nesting.
        int depth = 0;
        for (int i = 1; i < e.Stack.Count; i++)
            if (tracedMethods.Contains(e.Stack[i].Name)) depth++;
        return depth;
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
