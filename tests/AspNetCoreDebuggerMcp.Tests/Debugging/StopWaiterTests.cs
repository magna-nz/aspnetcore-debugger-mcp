using AspNetCoreDebuggerMcp.Debugging;

namespace AspNetCoreDebuggerMcp.Tests.Debugging;

public class StopWaiterTests
{
    [Fact]
    public async Task SetStop_CompletesPendingWait()
    {
        var w = new StopWaiter();
        var wait = w.WaitAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        w.SetStop(new StopInfo("breakpoint", 1, null));
        var info = await wait;
        Assert.Equal("breakpoint", info.Reason);
    }

    [Fact]
    public async Task Reset_BeforeStop_NextWaitSeesNewStop()
    {
        var w = new StopWaiter();

        // First cycle: stop arrives, wait returns.
        w.SetStop(new StopInfo("entry", 1, null));
        var first = await w.WaitAsync(CancellationToken.None);
        Assert.Equal("entry", first.Reason);

        // Reset to discard the completed TCS, then wait again.
        w.Reset();
        var second = w.WaitAsync(CancellationToken.None);
        Assert.False(second.IsCompleted);

        w.SetStop(new StopInfo("breakpoint", 2, null));
        Assert.Equal("breakpoint", (await second).Reason);
    }

    [Fact]
    public async Task Terminate_FaultsPendingWait()
    {
        var w = new StopWaiter();
        var wait = w.WaitAsync(CancellationToken.None);

        w.Terminate();

        await Assert.ThrowsAsync<DebugException>(() => wait);
    }

    [Fact]
    public void Terminate_SuppressesReset()
    {
        var w = new StopWaiter();
        w.Terminate();
        w.Reset();   // should not throw or revive
        // A new wait observes the terminated faulted TCS.
        var t = w.WaitAsync(CancellationToken.None);
        Assert.True(t.IsFaulted);
    }

    [Fact]
    public async Task SetStop_FirstWinsUntilReset()
    {
        var w = new StopWaiter();
        w.SetStop(new StopInfo("first", 1, null));
        w.SetStop(new StopInfo("second", 1, null));   // ignored — first already won

        var info = await w.WaitAsync(CancellationToken.None);
        Assert.Equal("first", info.Reason);
    }

    [Fact]
    public async Task Wait_CancelsWhenTokenCancels()
    {
        var w = new StopWaiter();
        using var cts = new CancellationTokenSource();
        var wait = w.WaitAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
