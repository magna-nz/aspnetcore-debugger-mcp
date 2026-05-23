using AspNetCoreDebuggerMcp.Diagnostics;

namespace AspNetCoreDebuggerMcp.Tests.Diagnostics;

public class TraceCollectorTests
{
    [Fact]
    public void Start_ThenStop_RoundTripsState()
    {
        var c = new TraceCollector();
        Assert.False(c.IsActive);

        c.Start(new[] { "Foo.Bar" }, captureStack: true, captureLocals: true,
            includeExceptions: true, maxFramesPerEvent: 10, maxLocalsPerFrame: 10);
        Assert.True(c.IsActive);

        var cfg = c.Stop();
        Assert.False(c.IsActive);
        Assert.Equal(new[] { "Foo.Bar" }, cfg!.Methods);
    }

    [Fact]
    public void Start_WhenAlreadyActive_Throws()
    {
        var c = new TraceCollector();
        c.Start(new[] { "Foo.Bar" }, true, true, true, 10, 10);
        Assert.Throws<InvalidOperationException>(() =>
            c.Start(new[] { "Baz" }, true, true, true, 10, 10));
    }

    [Fact]
    public void MatchesAnyTraceBp_OnlyMatchesRegisteredAdapterIds()
    {
        var c = new TraceCollector();
        c.Start(new[] { "Foo.Bar" }, true, true, true, 10, 10);
        c.SetAdapterIds(new[] { 7, 9 });

        Assert.True(c.MatchesAnyTraceBp(new[] { 7 }));
        Assert.True(c.MatchesAnyTraceBp(new[] { 1, 9 }));
        Assert.False(c.MatchesAnyTraceBp(new[] { 1, 2 }));
        Assert.False(c.MatchesAnyTraceBp(Array.Empty<int>()));
    }

    [Fact]
    public void Append_AddsToCurrentTrace()
    {
        var c = new TraceCollector();
        c.Start(new[] { "Foo.Bar" }, true, true, true, 10, 10);

        c.Append(new TraceEvent(5, TraceEventKind.Enter, 1, "Foo.Bar", null, null, null, null));
        c.Append(new TraceEvent(7, TraceEventKind.Enter, 1, "Foo.Baz", null, null, null, null));

        var events = c.Events();
        Assert.Equal(2, events.Count);
        Assert.Equal("Foo.Bar", events[0].Method);
    }

    [Fact]
    public void Append_WhenInactive_IsNoOp()
    {
        var c = new TraceCollector();
        c.Append(new TraceEvent(0, TraceEventKind.Enter, 1, "Foo.Bar", null, null, null, null));
        Assert.Empty(c.Events());
    }

    [Fact]
    public void Events_RespectsMaxAndReturnsLatestN()
    {
        var c = new TraceCollector();
        c.Start(new[] { "Foo.Bar" }, true, true, true, 10, 10);
        for (int i = 0; i < 5; i++)
            c.Append(new TraceEvent(i, TraceEventKind.Enter, 1, $"Method{i}", null, null, null, null));

        var last3 = c.Events(max: 3);
        Assert.Equal(3, last3.Count);
        Assert.Equal("Method2", last3[0].Method);
        Assert.Equal("Method4", last3[2].Method);
    }

    [Fact]
    public void ExceptionTracingEnabled_TracksFlag()
    {
        var c = new TraceCollector();
        c.Start(new[] { "Foo" }, true, true, includeExceptions: false, 10, 10);
        Assert.False(c.ExceptionTracingEnabled);

        c.Stop();
        c.Start(new[] { "Foo" }, true, true, includeExceptions: true, 10, 10);
        Assert.True(c.ExceptionTracingEnabled);
    }
}
