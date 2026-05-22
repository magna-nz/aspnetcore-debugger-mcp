using System.Text;
using AspNetCoreDebuggerMcp.Dap;

namespace AspNetCoreDebuggerMcp.Tests.Dap;

public class DapProtocolTests
{
    [Fact]
    public async Task WriteThenRead_RoundTripsSingleMessage()
    {
        using var stream = new MemoryStream();
        const string json = """{"seq":1,"type":"request","command":"initialize"}""";

        await DapProtocol.WriteMessageAsync(stream, json, CancellationToken.None);
        stream.Position = 0;
        var read = await DapProtocol.ReadMessageAsync(stream, CancellationToken.None);

        Assert.Equal(json, read);
    }

    [Fact]
    public async Task ReadMessageAsync_HandlesTwoFramedMessagesBackToBack()
    {
        using var stream = new MemoryStream();
        const string a = """{"seq":1,"type":"response"}""";
        const string b = """{"seq":2,"type":"event","event":"initialized"}""";

        await DapProtocol.WriteMessageAsync(stream, a, CancellationToken.None);
        await DapProtocol.WriteMessageAsync(stream, b, CancellationToken.None);
        stream.Position = 0;

        var first = await DapProtocol.ReadMessageAsync(stream, CancellationToken.None);
        var second = await DapProtocol.ReadMessageAsync(stream, CancellationToken.None);

        Assert.Equal(a, first);
        Assert.Equal(b, second);
    }

    [Fact]
    public async Task ReadMessageAsync_ReturnsNullOnCleanEndOfStream()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        var read = await DapProtocol.ReadMessageAsync(stream, CancellationToken.None);
        Assert.Null(read);
    }

    [Fact]
    public async Task ReadMessageAsync_ThrowsOnTruncatedBody()
    {
        var bytes = Encoding.ASCII.GetBytes("Content-Length: 100\r\n\r\nhello");
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            DapProtocol.ReadMessageAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadMessageAsync_HandlesUtf8MultiByteBody()
    {
        using var stream = new MemoryStream();
        const string json = """{"msg":"héllo →"}""";
        await DapProtocol.WriteMessageAsync(stream, json, CancellationToken.None);
        stream.Position = 0;

        var read = await DapProtocol.ReadMessageAsync(stream, CancellationToken.None);
        Assert.Equal(json, read);
    }
}
