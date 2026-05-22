using System.Text.Json;
using System.Text.Json.Serialization;

namespace AspNetCoreDebuggerMcp.Tools;

/// Shared JSON serializer + standard error envelope for MCP tool results.
internal static class ToolResults
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static string Err(Exception ex)
        => JsonSerializer.Serialize(new { success = false, error = ex.Message }, Json);
}
