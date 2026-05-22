using System.Text;

namespace AspNetCoreDebuggerMcp.Dap;

/// DAP message framing: each message is "Content-Length: N\r\n\r\n" + N bytes of UTF-8 JSON.
internal static class DapProtocol
{
    public static async Task WriteMessageAsync(Stream stream, string json, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// Reads one framed message. Returns null on clean end-of-stream.
    public static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var header = new List<byte>(64);
        var one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0) return null;
            header.Add(one[0]);
            int c = header.Count;
            if (c >= 4 && header[c - 4] == (byte)'\r' && header[c - 3] == (byte)'\n'
                       && header[c - 2] == (byte)'\r' && header[c - 1] == (byte)'\n')
                break;
        }

        int length = ParseContentLength(Encoding.ASCII.GetString(header.ToArray()));
        var body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = await stream.ReadAsync(body.AsMemory(read, length - read), ct).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("DAP body truncated");
            read += n;
        }
        return Encoding.UTF8.GetString(body);
    }

    private static int ParseContentLength(string header)
    {
        foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            const string key = "Content-Length:";
            if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return int.Parse(line[key.Length..].Trim());
        }
        throw new FormatException("DAP message missing Content-Length header");
    }
}
