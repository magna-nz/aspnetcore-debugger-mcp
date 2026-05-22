using System.Text.Json;
using AspNetCoreDebuggerMcp.Dap;

namespace AspNetCoreDebuggerMcp.Tests.Dap;

public class DapClientTests
{
    [Fact]
    public async Task SendRequest_CompletesWhenResponseWithMatchingSeqArrives()
    {
        await using var client = new DapClient(Stream.Null, new MemoryStream());

        var task = client.SendRequestAsync("initialize", null, CancellationToken.None);
        Assert.False(task.IsCompleted);

        client.HandleInbound(new DapMessage
        {
            Type = "response",
            RequestSeq = 1,
            Success = true,
            Command = "initialize",
        });

        var response = await task;
        Assert.True(response.Success);
        Assert.Equal("initialize", response.Command);
    }

    [Fact]
    public async Task SendRequest_WritesFramedJsonWithIncrementingSeqAndCommand()
    {
        var output = new MemoryStream();
        await using var client = new DapClient(Stream.Null, output);

        _ = client.SendRequestAsync("initialize", null, CancellationToken.None);
        _ = client.SendRequestAsync("launch", new { program = "/tmp/x.dll" }, CancellationToken.None);

        await Task.Delay(50);

        output.Position = 0;
        var a = await DapProtocol.ReadMessageAsync(output, CancellationToken.None);
        var b = await DapProtocol.ReadMessageAsync(output, CancellationToken.None);

        using var docA = JsonDocument.Parse(a!);
        using var docB = JsonDocument.Parse(b!);

        Assert.Equal("request", docA.RootElement.GetProperty("type").GetString());
        Assert.Equal("initialize", docA.RootElement.GetProperty("command").GetString());
        Assert.Equal(1, docA.RootElement.GetProperty("seq").GetInt32());

        Assert.Equal("launch", docB.RootElement.GetProperty("command").GetString());
        Assert.Equal(2, docB.RootElement.GetProperty("seq").GetInt32());
        Assert.Equal("/tmp/x.dll",
            docB.RootElement.GetProperty("arguments").GetProperty("program").GetString());
    }

    [Fact]
    public async Task EventReceived_FiresForEventMessages()
    {
        await using var client = new DapClient(Stream.Null, new MemoryStream());
        DapMessage? captured = null;
        client.EventReceived += m => captured = m;

        client.HandleInbound(new DapMessage { Type = "event", Event = "initialized" });

        Assert.NotNull(captured);
        Assert.Equal("initialized", captured!.Event);
    }

    [Fact]
    public async Task SendRequest_CancelsWhenTokenCancels()
    {
        await using var client = new DapClient(Stream.Null, new MemoryStream());
        using var cts = new CancellationTokenSource();

        var task = client.SendRequestAsync("initialize", null, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
