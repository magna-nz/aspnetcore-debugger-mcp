namespace AspNetCoreDebuggerMcp.Debugging;

/// Pure state machine for a debug session. Extracted from DebugSession so transitions
/// can be unit-tested without spinning up a real debugger.
///
/// Invariant: once Terminated, the session stays Terminated — late stopped/continued
/// events from a dying adapter must not resurrect it.
internal sealed class SessionStateMachine
{
    private readonly object _gate = new();
    private SessionState _state;

    public SessionStateMachine(SessionState initial = SessionState.Initializing)
        => _state = initial;

    public SessionState State { get { lock (_gate) return _state; } }

    public void Transition(SessionState target)
    {
        lock (_gate)
        {
            if (_state == SessionState.Terminated) return;
            _state = target;
        }
    }

    public void OnStopped()      => Transition(SessionState.Paused);
    public void OnContinued()    => Transition(SessionState.Running);
    public void OnTerminated()
    {
        lock (_gate) _state = SessionState.Terminated;
    }
}
