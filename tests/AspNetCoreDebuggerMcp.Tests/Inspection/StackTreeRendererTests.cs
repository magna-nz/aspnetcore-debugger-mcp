using AspNetCoreDebuggerMcp.Inspection;

namespace AspNetCoreDebuggerMcp.Tests.Inspection;

public class StackTreeRendererTests
{
    private static FrameWithLocals Frame(int id, string name, string? path, int? line, params (string Name, string Value)[] locals)
    {
        var vars = locals.Select(l => new VariableInfo(l.Name, l.Value, "string", 0, null, null, false)).ToList();
        var scopes = new List<ScopeWithVariables> { new("Locals", 100, false, vars) };
        return new FrameWithLocals(new StackFrame(id, name, path, line, null), scopes);
    }

    [Fact]
    public void EmptyFrames_ReturnsPlaceholder()
        => Assert.Equal("(no frames)", StackTreeRenderer.Render(Array.Empty<FrameWithLocals>()));

    [Fact]
    public void SingleFrame_ShowsPausedMarkerAndLocation()
    {
        var tree = StackTreeRenderer.Render(new[]
        {
            Frame(1, "Foo.Bar", "/x/Foo.cs", 12, ("x", "42")),
        });

        Assert.Contains("Foo.Bar   [Foo.cs:12]  ◄ paused here", tree);
        Assert.Contains("x = 42", tree);
        Assert.DoesNotContain("▼", tree);    // no arrow with a single frame
    }

    [Fact]
    public void MultipleFrames_RenderCallerFirstWithArrowsBetween()
    {
        // DAP stack order: index 0 is topmost (currently executing). Renderer must
        // emit OUTERMOST CALLER first, so a Controller → Service → Repository chain
        // reads naturally top-to-bottom.
        var frames = new[]
        {
            Frame(1, "Repo.GetByIdAsync",   "/r/Repo.cs",  23, ("id", "42"), ("row", "null")),
            Frame(2, "Svc.GetUserAsync",    "/s/Svc.cs",   67, ("id", "42"), ("_repo", "UserRepository")),
            Frame(3, "Ctrl.GetUser",        "/c/Ctrl.cs",  42, ("id", "42")),
        };

        var tree = StackTreeRenderer.Render(frames);

        // Controller (the outermost caller) comes first.
        var ctrlIdx = tree.IndexOf("Ctrl.GetUser", StringComparison.Ordinal);
        var svcIdx  = tree.IndexOf("Svc.GetUserAsync", StringComparison.Ordinal);
        var repoIdx = tree.IndexOf("Repo.GetByIdAsync", StringComparison.Ordinal);

        Assert.True(ctrlIdx >= 0 && svcIdx > ctrlIdx && repoIdx > svcIdx,
            $"expected order Ctrl < Svc < Repo, got {ctrlIdx}/{svcIdx}/{repoIdx} in:\n{tree}");

        Assert.Contains("▼", tree);                          // arrows between frames
        Assert.Contains("Repo.GetByIdAsync", tree);
        Assert.Contains("◄ paused here", tree);
        // Marker is on the currently-paused frame only.
        var firstMarker = tree.IndexOf("◄ paused here", StringComparison.Ordinal);
        Assert.True(firstMarker > svcIdx, "paused marker must be on the topmost (last printed) frame");
    }

    [Fact]
    public void Frame_WithoutSource_OmitsLocation()
    {
        var tree = StackTreeRenderer.Render(new[]
        {
            Frame(1, "External.Frame", path: null, line: null),
        });
        Assert.Contains("External.Frame", tree);
        Assert.DoesNotContain("[", tree);
    }

    [Fact]
    public void Frame_WithEmptyScopes_OmitsLocalsLine()
    {
        var f = new FrameWithLocals(
            new StackFrame(1, "Foo.Bar", "/Foo.cs", 1, null),
            Array.Empty<ScopeWithVariables>());

        var tree = StackTreeRenderer.Render(new[] { f });
        Assert.Contains("Foo.Bar", tree);
        Assert.DoesNotContain("=", tree);   // no key=value line at all
    }

    [Fact]
    public void LongValue_IsTruncated()
    {
        var huge = new string('x', 200);
        var tree = StackTreeRenderer.Render(new[]
        {
            Frame(1, "Foo", "/x.cs", 1, ("s", huge)),
        });
        Assert.Contains("…", tree);
        Assert.DoesNotContain(huge, tree);
    }
}
