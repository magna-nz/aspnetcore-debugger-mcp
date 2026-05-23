using AspNetCoreDebuggerMcp.Diagnostics;
using AspNetCoreDebuggerMcp.Inspection;

namespace AspNetCoreDebuggerMcp.Tests.Diagnostics;

public class TraceRendererTests
{
    private static VariableInfo Var(string name, string value)
        => new(name, value, "string", 0, null, null, false);

    private static StackFrame Sf(int id, string name, string? path = null, int? line = null)
        => new(id, name, path, line, null);

    [Fact]
    public void Empty_ReturnsPlaceholder()
        => Assert.Equal("(no trace events)", TraceRenderer.Render(Array.Empty<TraceEvent>()));

    [Fact]
    public void Enter_ShowsArrowAndLocals()
    {
        var ev = new TraceEvent(5, TraceEventKind.Enter, 1, "Foo.Bar", null, null,
            new[] { Var("id", "42") }, new[] { Sf(1, "Foo.Bar") });
        var output = TraceRenderer.Render(new[] { ev });

        Assert.Contains("→ Foo.Bar", output);
        Assert.Contains("id=42", output);
        Assert.Contains("[+    5ms]", output);
    }

    [Fact]
    public void Exception_ShowsWarningMarkerAndTypeAndMessage()
    {
        var ev = new TraceEvent(12, TraceEventKind.Exception, 1, null,
            "System.NullReferenceException", "Object reference not set", null,
            new[] { Sf(1, "Foo.Bar", "/Foo.cs", 7) });
        var output = TraceRenderer.Render(new[] { ev });

        Assert.Contains("⚠ System.NullReferenceException: Object reference not set", output);
        Assert.Contains("at Foo.Bar", output);
        Assert.Contains("[Foo.cs:7]", output);
    }

    [Fact]
    public void IndentsByRelativeStackDepth()
    {
        // Three calls: depth 3, 4, 5. Output should show 0, 2, 4 spaces of indent.
        var e1 = new TraceEvent(0, TraceEventKind.Enter, 1, "Ctrl",
            null, null, null, new[] { Sf(1,"Ctrl"), Sf(2,"K1"), Sf(3,"K2") });          // depth 3
        var e2 = new TraceEvent(2, TraceEventKind.Enter, 1, "Svc",
            null, null, null, new[] { Sf(1,"Svc"), Sf(2,"Ctrl"), Sf(3,"K1"), Sf(4,"K2") }); // depth 4
        var e3 = new TraceEvent(5, TraceEventKind.Enter, 1, "Repo",
            null, null, null, new[] { Sf(1,"Repo"), Sf(2,"Svc"), Sf(3,"Ctrl"), Sf(4,"K1"), Sf(5,"K2") }); // depth 5

        var output = TraceRenderer.Render(new[] { e1, e2, e3 });
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Strip the "[+    Nms] " prefix to inspect the indentation.
        var bodies = lines.Select(l =>
        {
            int p = l.IndexOf(']');
            return p >= 0 ? l[(p + 2)..] : l;
        }).ToList();

        Assert.StartsWith("→ Ctrl", bodies[0]);
        Assert.StartsWith("  → Svc", bodies[1]);
        Assert.StartsWith("    → Repo", bodies[2]);
    }

    [Fact]
    public void LongLocalValueIsTruncated()
    {
        var huge = new string('x', 200);
        var ev = new TraceEvent(0, TraceEventKind.Enter, 1, "Foo", null, null,
            new[] { Var("s", huge) }, new[] { Sf(1, "Foo") });
        var output = TraceRenderer.Render(new[] { ev });
        Assert.Contains("…", output);
        Assert.DoesNotContain(huge, output);
    }
}
