using AspNetCoreDebuggerMcp.Diagnostics;

namespace AspNetCoreDebuggerMcp.Tests.Diagnostics;

public class ThreadAnalyzerTests
{
    [Theory]
    [InlineData("System.Threading.Monitor.Wait(object)", BlockingKind.BlockedOnMonitor)]
    [InlineData("System.Threading.Monitor.Enter(object)", BlockingKind.BlockedOnMonitor)]
    [InlineData("System.Threading.SemaphoreSlim.Wait()", BlockingKind.BlockedOnSemaphore)]
    [InlineData("System.Threading.WaitHandle.WaitOne()", BlockingKind.BlockedOnWaitHandle)]
    [InlineData("System.Threading.ManualResetEvent.WaitOne()", BlockingKind.BlockedOnWaitHandle)]
    [InlineData("System.Threading.Tasks.Task.Wait()", BlockingKind.BlockedOnTask)]
    [InlineData("System.Runtime.CompilerServices.TaskAwaiter.GetResult()", BlockingKind.BlockedOnTask)]
    [InlineData("System.Threading.Thread.Join()", BlockingKind.BlockedOnThreadJoin)]
    [InlineData("System.Threading.Thread.Sleep(int)", BlockingKind.Sleeping)]
    [InlineData("System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted", BlockingKind.AwaitingAsync)]
    [InlineData("MyApp.Program.<Main>$(string[])", BlockingKind.Running)]
    public void Classify_FromTopFrameName(string topFrame, BlockingKind expected)
    {
        Assert.Equal(expected, ThreadAnalyzer.Classify(new[] { topFrame }));
    }

    [Fact]
    public void Classify_PrefersTopmostFrame()
    {
        // Monitor.Wait is deeper in stack, but Sleep is on top → Sleeping wins.
        var frames = new[]
        {
            "System.Threading.Thread.Sleep(int)",
            "System.Threading.Monitor.Wait(object)",
            "MyApp.Worker.Run()",
        };
        Assert.Equal(BlockingKind.Sleeping, ThreadAnalyzer.Classify(frames));
    }

    [Fact]
    public void Analyze_CountsBlockedThreadsAndAddsCycleNote()
    {
        var threads = new[]
        {
            new ThreadHangInfo(1, "Main", BlockingKind.Running, Array.Empty<string>()),
            new ThreadHangInfo(2, "Worker1", BlockingKind.BlockedOnMonitor, Array.Empty<string>()),
            new ThreadHangInfo(3, "Worker2", BlockingKind.BlockedOnTask, Array.Empty<string>()),
        };
        var result = ThreadAnalyzer.Analyze(threads);
        Assert.Equal(2, result.BlockedCount);
        Assert.False(result.CycleDetectionAvailable);
        Assert.Contains("Cycle detection", result.Notes);
    }

    [Fact]
    public void Analyze_NoBlockedThreads_HasNotice()
    {
        var threads = new[] { new ThreadHangInfo(1, "Main", BlockingKind.Running, Array.Empty<string>()) };
        var result = ThreadAnalyzer.Analyze(threads);
        Assert.Equal(0, result.BlockedCount);
        Assert.Contains("No threads appear blocked", result.Notes);
    }
}
