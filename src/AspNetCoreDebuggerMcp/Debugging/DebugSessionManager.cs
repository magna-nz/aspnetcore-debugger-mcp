namespace AspNetCoreDebuggerMcp.Debugging;

/// Holds the single active debug session. Serializes all session-lifecycle calls
/// (launch/attach/disconnect) so concurrent tool invocations cannot race.
public sealed class DebugSessionManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DebugSession? _session;

    public async Task<SessionSnapshot> LaunchAsync(
        string program, string[]? args, string? cwd, bool stopAtEntry, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureNoActiveSession();
            await DisposeTerminatedAsync().ConfigureAwait(false);
            var path = NetcoredbgLocator.Locate();
            _session = await DebugSession.LaunchAsync(path, program, args, cwd, stopAtEntry, ct)
                .ConfigureAwait(false);
            return _session.Snapshot();
        }
        finally { _gate.Release(); }
    }

    public async Task<SessionSnapshot> AttachAsync(int processId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureNoActiveSession();
            await DisposeTerminatedAsync().ConfigureAwait(false);
            var path = NetcoredbgLocator.Locate();
            _session = await DebugSession.AttachAsync(path, processId, ct).ConfigureAwait(false);
            return _session.Snapshot();
        }
        finally { _gate.Release(); }
    }

    public async Task<SessionSnapshot> DisconnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null) return SessionSnapshot.None;
            try { await _session.DisconnectAsync(ct).ConfigureAwait(false); } catch { }
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
            return SessionSnapshot.None;
        }
        finally { _gate.Release(); }
    }

    public SessionSnapshot GetState()
        => _session is null ? SessionSnapshot.None : _session.Snapshot();

    private void EnsureNoActiveSession()
    {
        if (_session is not null && _session.State != SessionState.Terminated)
            throw new DebugException(
                "A debug session is already active. Call debug_disconnect before starting a new one.");
    }

    private async Task DisposeTerminatedAsync()
    {
        if (_session is null) return;
        try { await _session.DisposeAsync().ConfigureAwait(false); } catch { }
        _session = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            try { await _session.DisposeAsync().ConfigureAwait(false); } catch { }
            _session = null;
        }
        _gate.Dispose();
    }
}
