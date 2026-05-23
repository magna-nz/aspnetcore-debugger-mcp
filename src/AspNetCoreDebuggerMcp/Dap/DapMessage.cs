using System.Text.Json;

namespace AspNetCoreDebuggerMcp.Dap;

/// A parsed DAP protocol message (response or event).
internal sealed class DapMessage
{
    public int Seq { get; init; }
    public string Type { get; init; } = "";   // "response" | "event" | "request"

    // response-only
    public int RequestSeq { get; init; }
    public bool Success { get; init; }
    public string? Command { get; init; }
    public string? Message { get; init; }

    // event-only
    public string? Event { get; init; }

    // payload (response.body or event.body)
    public JsonElement? Body { get; init; }

    public static DapMessage Parse(JsonElement root)
    {
        var type = root.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";
        return new DapMessage
        {
            Seq = root.TryGetProperty("seq", out var s) ? s.GetInt32() : 0,
            Type = type,
            RequestSeq = root.TryGetProperty("request_seq", out var rs) ? rs.GetInt32() : 0,
            Success = root.TryGetProperty("success", out var su) && su.ValueKind == JsonValueKind.True,
            Command = root.TryGetProperty("command", out var c) ? c.GetString() : null,
            Message = root.TryGetProperty("message", out var m) ? m.GetString() : null,
            Event = root.TryGetProperty("event", out var e) ? e.GetString() : null,
            // Clone so the element remains valid after the source JsonDocument is disposed.
            Body = root.TryGetProperty("body", out var b) ? b.Clone() : null,
        };
    }
}
