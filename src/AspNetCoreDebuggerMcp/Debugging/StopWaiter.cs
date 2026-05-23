namespace AspNetCoreDebuggerMcp.Debugging;

/// One-shot waiter for the *next* stop event after a continue/step.
///
/// Reset() must be called synchronously inside Continue/Step (before the DAP request is sent)
/// so that a wait registered immediately afterward sees a fresh, uncompleted TCS. If we instead
/// reset on the `continued` event we'd race the agent's wait: it could observe the previous
/// (completed) stop's TCS and return immediately with stale info.
internal sealed class StopWaiter
{
    private readonly object _gate = new();
    private TaskCompletionSource<StopInfo> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _terminated;

    /// Replace the TCS with a fresh uncompleted one. No-op once terminated.
    public void Reset()
    {
        lock (_gate)
        {
            if (_terminated) return;
            _tcs = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// Complete the current TCS with the stop information. Subsequent SetStop calls (until Reset)
    /// are no-ops; the first stop wins.
    public void SetStop(StopInfo info)
    {
        TaskCompletionSource<StopInfo> tcs;
        lock (_gate) tcs = _tcs;
        tcs.TrySetResult(info);
    }

    /// Mark as terminated; the current pending TCS is faulted and any further Reset is suppressed.
    public void Terminate()
    {
        lock (_gate)
        {
            _terminated = true;
            _tcs.TrySetException(new DebugException("Debug session terminated."));
        }
    }

    /// Wait for the current TCS to complete, honouring the cancellation token.
    public Task<StopInfo> WaitAsync(CancellationToken ct)
    {
        TaskCompletionSource<StopInfo> tcs;
        lock (_gate) tcs = _tcs;
        return tcs.Task.WaitAsync(ct);
    }
}
