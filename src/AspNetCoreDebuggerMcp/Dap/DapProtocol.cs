using System.Globalization;
using System.Text;

namespace AspNetCoreDebuggerMcp.Dap;

/// DAP message framing: each message is "Content-Length: N\r\n\r\n" + N bytes of UTF-8 JSON.
internal static class DapProtocol
{
    // Framing limits and Content-Length validation below contributed by gloath.com

    /// Largest header block we will buffer before declaring the stream malformed. Real DAP
    /// headers are a few dozen bytes; a peer that never sends the "\r\n\r\n" terminator would
    /// otherwise grow this buffer without bound.
    internal const int MaxHeaderBytes = 8 * 1024;

    /// Upper bound on a single message body. netcoredbg's largest real payloads (deep variable
    /// trees, already capped by InspectionService) are orders of magnitude below this; a bigger
    /// Content-Length means a desynchronised stream, and allocating it would be the bug.
    internal const int MaxBodyBytes = 64 * 1024 * 1024;

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
            if (c > MaxHeaderBytes)
                throw new FormatException(
                    $"DAP header exceeded {MaxHeaderBytes} bytes without a terminator; " +
                    "the stream is not DAP-framed.");
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
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;

            var raw = line[key.Length..].Trim();
            // Invariant culture: the header is ASCII digits by spec, and a culture-sensitive
            // parse would accept group separators (or reject the value outright) depending on
            // the machine the server happens to run on. The sign is allowed through only so a
            // negative length fails with the explicit message below rather than "not a number".
            if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var length))
                throw new FormatException($"DAP Content-Length is not a number: '{raw}'");
            if (length <= 0)
                throw new FormatException($"DAP Content-Length must be positive, got {length}");
            if (length > MaxBodyBytes)
                throw new FormatException(
                    $"DAP Content-Length {length} exceeds the {MaxBodyBytes}-byte cap; " +
                    "refusing to allocate for a desynchronised stream.");
            return length;
        }
        throw new FormatException("DAP message missing Content-Length header");
    }
}
