using AspNetCoreDebuggerMcp.Breakpoints;

namespace AspNetCoreDebuggerMcp.Tests.Breakpoints;

public class BreakpointRegistryTests
{
    [Fact]
    public void AddLine_AssignsIdAndAppearsInSnapshot()
    {
        var r = new BreakpointRegistry();
        var bp = r.AddLine("/repo/Program.cs", 5, null, null, null);

        Assert.StartsWith("bp-", bp.Id);
        Assert.False(bp.Verified);

        var snap = r.Snapshot();
        Assert.Single(snap.Line);
        Assert.Equal(bp.Id, snap.Line[0].Id);
    }

    [Fact]
    public void Remove_RemovesAndReturnsKind()
    {
        var r = new BreakpointRegistry();
        var l = r.AddLine("/x.cs", 1, null, null, null);
        var f = r.AddFunction("Foo.Bar", null, null);

        Assert.Equal(BreakpointKind.Line, r.Remove(l.Id));
        Assert.Equal(BreakpointKind.Function, r.Remove(f.Id));
        Assert.Null(r.Remove("bp-deadbeef"));

        var snap = r.Snapshot();
        Assert.Empty(snap.Line);
        Assert.Empty(snap.Function);
    }

    [Fact]
    public void ForSource_FiltersByPathExactly()
    {
        var r = new BreakpointRegistry();
        r.AddLine("/a.cs", 1, null, null, null);
        r.AddLine("/a.cs", 2, null, null, null);
        r.AddLine("/b.cs", 1, null, null, null);

        Assert.Equal(2, r.ForSource("/a.cs").Count);
        Assert.Single(r.ForSource("/b.cs"));
        Assert.Empty(r.ForSource("/c.cs"));
    }

    [Fact]
    public void Sources_ReturnsDistinctPaths()
    {
        var r = new BreakpointRegistry();
        r.AddLine("/a.cs", 1, null, null, null);
        r.AddLine("/a.cs", 2, null, null, null);
        r.AddLine("/b.cs", 1, null, null, null);

        var sources = r.Sources();
        Assert.Equal(2, sources.Count);
        Assert.Contains("/a.cs", sources);
        Assert.Contains("/b.cs", sources);
    }

    [Fact]
    public void UpdateLine_SetsVerifiedAndAdapterId()
    {
        var r = new BreakpointRegistry();
        var bp = r.AddLine("/a.cs", 1, null, null, null);

        r.UpdateLine(bp.Id, verified: true, adapterId: 42);

        var updated = r.Snapshot().Line.Single();
        Assert.True(updated.Verified);
        Assert.Equal(42, updated.AdapterId);
    }

    [Fact]
    public void UpdateLineByAdapterId_FlipsVerifiedForMatchingBreakpoint()
    {
        var r = new BreakpointRegistry();
        var a = r.AddLine("/a.cs", 1, null, null, null);
        var b = r.AddLine("/a.cs", 2, null, null, null);
        r.UpdateLine(a.Id, false, adapterId: 7);
        r.UpdateLine(b.Id, false, adapterId: 8);

        r.UpdateLineByAdapterId(7, verified: true);

        var snap = r.Snapshot().Line;
        Assert.True(snap.Single(bp => bp.AdapterId == 7).Verified);
        Assert.False(snap.Single(bp => bp.AdapterId == 8).Verified);
    }

    [Fact]
    public void SetExceptionFilters_ReplacesFullSet()
    {
        var r = new BreakpointRegistry();
        r.SetExceptionFilters(new[] { "all" });
        r.SetExceptionFilters(new[] { "user-unhandled" });

        Assert.Equal(new[] { "user-unhandled" }, r.Snapshot().ExceptionFilters);
    }
}
