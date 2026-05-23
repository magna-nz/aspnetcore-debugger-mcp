namespace AspNetCoreDebuggerMcp.Diagnostics;

public enum BlockingKind
{
    Running,
    BlockedOnMonitor,        // Monitor.Wait / Monitor.Enter
    BlockedOnWaitHandle,     // WaitHandle.WaitOne / WaitAll / WaitAny / ManualResetEvent / AutoResetEvent
    BlockedOnSemaphore,      // SemaphoreSlim.Wait / Semaphore.WaitOne
    BlockedOnTask,           // Task.Wait / Task.Result / GetAwaiter().GetResult
    BlockedOnThreadJoin,     // Thread.Join
    Sleeping,                // Thread.Sleep / Task.Delay (sync wait)
    AwaitingAsync,           // AsyncTaskMethodBuilder / state machine MoveNext awaiting
}

public sealed record ThreadHangInfo(int ThreadId, string Name, BlockingKind BlockingKind, IReadOnlyList<string> TopFrameNames);

public sealed record HangAnalysis(
    IReadOnlyList<ThreadHangInfo> Threads,
    int BlockedCount,
    bool CycleDetectionAvailable,
    string Notes);

/// Pure stack-frame classifier. Looks at the top N frame names of a thread and
/// returns the most-likely blocking pattern. The first frame is treated as the
/// most recent (topmost) call.
public static class ThreadAnalyzer
{
    public static BlockingKind Classify(IReadOnlyList<string> topFrameNames)
    {
        foreach (var raw in topFrameNames)
        {
            var name = raw ?? "";
            if (Contains(name, "Monitor.Wait") || Contains(name, "Monitor.Enter") ||
                Contains(name, "Monitor.TryEnter") || Contains(name, "lock(")) return BlockingKind.BlockedOnMonitor;

            if (Contains(name, "SemaphoreSlim.Wait") || Contains(name, "Semaphore.WaitOne")) return BlockingKind.BlockedOnSemaphore;

            if (Contains(name, "WaitHandle.Wait") || Contains(name, "ManualResetEvent.WaitOne") ||
                Contains(name, "AutoResetEvent.WaitOne") || Contains(name, "ManualResetEventSlim.Wait")) return BlockingKind.BlockedOnWaitHandle;

            if (Contains(name, "Task.Wait") || Contains(name, "Task`1.get_Result") ||
                Contains(name, "TaskAwaiter.GetResult") || Contains(name, "GetAwaiter().GetResult")) return BlockingKind.BlockedOnTask;

            if (Contains(name, "Thread.Join")) return BlockingKind.BlockedOnThreadJoin;
            if (Contains(name, "Thread.Sleep") || Contains(name, "Task.Delay")) return BlockingKind.Sleeping;

            if (Contains(name, "AwaitUnsafeOnCompleted") || Contains(name, "AsyncTaskMethodBuilder")) return BlockingKind.AwaitingAsync;
        }
        return BlockingKind.Running;
    }

    public static HangAnalysis Analyze(IEnumerable<ThreadHangInfo> threads)
    {
        var list = threads.ToList();
        var blocked = list.Count(t => t.BlockingKind != BlockingKind.Running);
        return new HangAnalysis(
            Threads: list,
            BlockedCount: blocked,
            CycleDetectionAvailable: false,
            Notes: blocked == 0
                ? "No threads appear blocked on a known synchronization primitive."
                : "Cycle detection requires lock-ownership data which DAP does not expose. Inspect the stacks to determine causality.");
    }

    private static bool Contains(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.Ordinal) >= 0;
}
