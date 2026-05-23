using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AspNetCoreDebuggerMcp.Dap;

/// Hand-rolled DAP client: speaks Content-Length framed JSON over a pair of streams,
/// correlates responses to requests by `seq`, and dispatches events to subscribers.
internal sealed class DapClient : IAsyncDisposable
{
    // netcoredbg's DAP parser rejects null values where a string is expected; omit them.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<DapMessage>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private int _seq;
    private Task? _readLoop;

    public event Action<DapMessage>? EventReceived;

    public DapClient(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// Start the background read loop. Tests can omit this and drive HandleInbound directly.
    public void Start() => _readLoop ??= Task.Run(ReadLoopAsync);

    public async Task<DapMessage> SendRequestAsync(string command, object? arguments, CancellationToken ct)
    {
        int seq = Interlocked.Increment(ref _seq);
        var tcs = new TaskCompletionSource<DapMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(seq, tcs))
            throw new InvalidOperationException($"seq {seq} already in use");

        var envelope = new Dictionary<string, object?>
        {
            ["seq"] = seq,
            ["type"] = "request",
            ["command"] = command,
        };
        if (arguments is not null) envelope["arguments"] = arguments;

        var json = JsonSerializer.Serialize(envelope, JsonOpts);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DapProtocol.WriteMessageAsync(_output, json, ct).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }

        using (ct.Register(() =>
        {
            if (_pending.TryRemove(seq, out var t)) t.TrySetCanceled(ct);
        }))
        {
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    /// Visible for tests: dispatch a received message exactly as the read loop would.
    internal void HandleInbound(DapMessage message)
    {
        switch (message.Type)
        {
            case "response":
                if (_pending.TryRemove(message.RequestSeq, out var tcs))
                    tcs.TrySetResult(message);
                break;
            case "event":
                EventReceived?.Invoke(message);
                break;
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var raw = await DapProtocol.ReadMessageAsync(_input, _cts.Token).ConfigureAwait(false);
                if (raw is null) break;
                using var doc = JsonDocument.Parse(raw);
                HandleInbound(DapMessage.Parse(doc.RootElement));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* stream broke — fall through to fail pending */ }
        finally
        {
            foreach (var kv in _pending)
                kv.Value.TrySetException(new IOException("DAP connection closed"));
            _pending.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); } catch { }
        }
        _cts.Dispose();
        _writeLock.Dispose();
    }
}
