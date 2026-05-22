using AspNetCoreDebuggerMcp.Debugging;

namespace AspNetCoreDebuggerMcp.Tests.Debugging;

public class SessionStateMachineTests
{
    [Fact]
    public void DefaultInitialState_IsInitializing()
    {
        var sm = new SessionStateMachine();
        Assert.Equal(SessionState.Initializing, sm.State);
    }

    [Fact]
    public void Transition_MovesToTargetState()
    {
        var sm = new SessionStateMachine();
        sm.Transition(SessionState.Configuring);
        Assert.Equal(SessionState.Configuring, sm.State);
        sm.Transition(SessionState.Running);
        Assert.Equal(SessionState.Running, sm.State);
    }

    [Fact]
    public void OnStopped_MovesRunningToPaused()
    {
        var sm = new SessionStateMachine(SessionState.Running);
        sm.OnStopped();
        Assert.Equal(SessionState.Paused, sm.State);
    }

    [Fact]
    public void OnContinued_MovesPausedToRunning()
    {
        var sm = new SessionStateMachine(SessionState.Paused);
        sm.OnContinued();
        Assert.Equal(SessionState.Running, sm.State);
    }

    [Fact]
    public void OnTerminated_IsSticky_LateEventsCannotResurrect()
    {
        var sm = new SessionStateMachine(SessionState.Running);
        sm.OnTerminated();
        Assert.Equal(SessionState.Terminated, sm.State);

        sm.OnStopped();
        sm.OnContinued();
        sm.Transition(SessionState.Running);

        Assert.Equal(SessionState.Terminated, sm.State);
    }
}
