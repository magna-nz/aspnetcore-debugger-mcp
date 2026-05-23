using System.Text;
using System.Text.Json;
using AspNetCoreDebuggerMcp.Dap;
using AspNetCoreDebuggerMcp.Debugging;

namespace AspNetCoreDebuggerMcp.Inspection;

/// Per-session adapter that translates DAP threads / stackTrace / scopes / variables /
/// evaluate / setExpression / exceptionInfo into our typed records, with smart variable
/// expansion (depth + max-children truncation) and an exception_autopsy composite.
internal sealed class InspectionService
{
    private readonly DapClient _client;

    public InspectionService(DapClient client) => _client = client;

    // ---- threads + stack -----------------------------------------------------------

    public async Task<IReadOnlyList<ThreadInfo>> ListThreadsAsync(CancellationToken ct)
    {
        var resp = await _client.SendRequestAsync("threads", null, ct).ConfigureAwait(false);
        if (!resp.Success) throw new DebugException($"threads failed: {resp.Message ?? "unknown"}");

        var list = new List<ThreadInfo>();
        if (resp.Body is { } body && body.TryGetProperty("threads", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                var id = ReadInt(t, "id") ?? 0;
                var name = ReadString(t, "name") ?? "";
                list.Add(new ThreadInfo(id, name));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<StackFrame>> GetStackTraceAsync(
        int threadId, int? startFrame, int? levels, bool raw, CancellationToken ct)
    {
        var args = new Dictionary<string, object?> { ["threadId"] = threadId };
        if (startFrame is int s) args["startFrame"] = s;
        if (levels is int l) args["levels"] = l;
        var resp = await _client.SendRequestAsync("stackTrace", args, ct).ConfigureAwait(false);
        if (!resp.Success) throw new DebugException($"stackTrace failed: {resp.Message ?? "unknown"}");

        var list = new List<StackFrame>();
        if (resp.Body is { } body && body.TryGetProperty("stackFrames", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in arr.EnumerateArray()) list.Add(ParseFrame(f));
        }

        return raw ? list : AsyncStackFlattener.Flatten(list, hideInfrastructure: true);
    }

    public async Task<StackFrame?> GetTopFrameAsync(int threadId, CancellationToken ct)
    {
        // Always flatten the top frame name so auto-context shows "Foo.BarAsync" instead of
        // "Foo.<BarAsync>d__3.MoveNext". Single frame, so no infrastructure to skip.
        var frames = await GetStackTraceAsync(threadId, 0, 1, raw: false, ct).ConfigureAwait(false);
        return frames.Count > 0 ? frames[0] : null;
    }

    private static StackFrame ParseFrame(JsonElement f)
    {
        var id = ReadInt(f, "id") ?? 0;
        var name = ReadString(f, "name") ?? "";
        string? path = null;
        if (f.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Object)
            path = ReadString(src, "path");
        var line = ReadInt(f, "line");
        var col = ReadInt(f, "column");
        return new StackFrame(id, name, path, line, col);
    }

    // ---- scopes + variables --------------------------------------------------------

    public async Task<IReadOnlyList<ScopeWithVariables>> GetScopesAsync(
        int frameId, int depth, int maxChildren, CancellationToken ct)
    {
        var resp = await _client.SendRequestAsync("scopes", new { frameId }, ct).ConfigureAwait(false);
        if (!resp.Success) throw new DebugException($"scopes failed: {resp.Message ?? "unknown"}");

        var scopes = new List<ScopeWithVariables>();
        if (resp.Body is { } body && body.TryGetProperty("scopes", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in arr.EnumerateArray())
            {
                var name = ReadString(s, "name") ?? "";
                var varRef = ReadInt(s, "variablesReference") ?? 0;
                var expensive = s.TryGetProperty("expensive", out var ex) && ex.ValueKind == JsonValueKind.True;
                var vars = varRef > 0
                    ? await FetchVariablesAsync(varRef, depth, maxChildren, ct).ConfigureAwait(false)
                    : Array.Empty<VariableInfo>();
                scopes.Add(new ScopeWithVariables(name, varRef, expensive, vars));
            }
        }
        return scopes;
    }

    /// Fetch the variables for a container reference, recursively up to `remainingDepth` levels.
    /// At depth 1 we fetch this container's immediate children but no further; at depth 2 we
    /// also fetch grandchildren; etc.
    private async Task<IReadOnlyList<VariableInfo>> FetchVariablesAsync(
        int variablesReference, int remainingDepth, int maxChildren, CancellationToken ct)
    {
        var resp = await _client.SendRequestAsync("variables",
            new { variablesReference }, ct).ConfigureAwait(false);
        if (!resp.Success) return Array.Empty<VariableInfo>();

        var all = new List<JsonElement>();
        if (resp.Body is { } body && body.TryGetProperty("variables", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in arr.EnumerateArray()) all.Add(v);
        }

        var taken = all.Count > maxChildren ? all.Take(maxChildren).ToList() : all;
        var truncated = all.Count > maxChildren;
        var totalIfTruncated = truncated ? (int?)all.Count : null;

        var results = new List<VariableInfo>(taken.Count);
        foreach (var v in taken)
        {
            var name = ReadString(v, "name") ?? "";
            var value = ReadString(v, "value") ?? "";
            var type = ReadString(v, "type");
            var childRef = ReadInt(v, "variablesReference") ?? 0;
            IReadOnlyList<VariableInfo>? children = null;
            int? childTotal = null;
            bool childTrunc = false;
            if (childRef > 0 && remainingDepth > 1)
            {
                children = await FetchVariablesAsync(childRef, remainingDepth - 1, maxChildren, ct)
                    .ConfigureAwait(false);
            }
            results.Add(new VariableInfo(name, value, type, childRef, children, childTotal, childTrunc));
        }

        // Truncation info for THIS level is recorded on the parent (the caller's scope/var).
        // For the scope itself, GetScopesAsync attaches the list as-is — callers can detect
        // truncation by comparing the returned count to maxChildren and/or by the per-VariableInfo
        // TotalChildren on parents.
        return results;
    }

    // ---- evaluate + set ------------------------------------------------------------

    public async Task<EvaluateResult> EvaluateAsync(string expression, int? frameId, CancellationToken ct)
    {
        var args = new Dictionary<string, object?>
        {
            ["expression"] = expression,
            ["context"] = "repl",
        };
        if (frameId is int fid) args["frameId"] = fid;

        var resp = await _client.SendRequestAsync("evaluate", args, ct).ConfigureAwait(false);
        if (!resp.Success) throw new DebugException($"evaluate failed: {resp.Message ?? "unknown"}");

        var body = resp.Body!.Value;
        return new EvaluateResult(
            ReadString(body, "result") ?? "",
            ReadString(body, "type"),
            ReadInt(body, "variablesReference") ?? 0);
    }

    public async Task<EvaluateResult> SetExpressionAsync(
        string expression, string value, int? frameId, CancellationToken ct)
    {
        var args = new Dictionary<string, object?>
        {
            ["expression"] = expression,
            ["value"] = value,
        };
        if (frameId is int fid) args["frameId"] = fid;

        var resp = await _client.SendRequestAsync("setExpression", args, ct).ConfigureAwait(false);
        if (!resp.Success) throw new DebugException($"setExpression failed: {resp.Message ?? "unknown"}");

        var body = resp.Body!.Value;
        return new EvaluateResult(
            ReadString(body, "value") ?? "",
            ReadString(body, "type"),
            ReadInt(body, "variablesReference") ?? 0);
    }

    // ---- exception autopsy ---------------------------------------------------------

    public async Task<ExceptionAutopsy> AutopsyAsync(int threadId, int topFrameCount, CancellationToken ct)
    {
        string? exceptionId = null, description = null, breakMode = null;
        var layers = new List<ExceptionLayer>();

        try
        {
            var resp = await _client.SendRequestAsync("exceptionInfo",
                new { threadId }, ct).ConfigureAwait(false);
            if (resp.Success && resp.Body is { } body)
            {
                exceptionId = ReadString(body, "exceptionId");
                description = ReadString(body, "description");
                breakMode = ReadString(body, "breakMode");
                if (body.TryGetProperty("details", out var details)
                    && details.ValueKind == JsonValueKind.Object)
                {
                    CollectLayers(details, layers);
                }
            }
        }
        catch { /* exceptionInfo may not be available; continue with what we have */ }

        var frames = await GetStackTraceAsync(threadId, 0, topFrameCount, raw: false, ct).ConfigureAwait(false);

        IReadOnlyList<ScopeWithVariables>? topLocals = null;
        SourceSnippet? snippet = null;
        if (frames.Count > 0)
        {
            try
            {
                topLocals = await GetScopesAsync(frames[0].Id, depth: 1, maxChildren: 50, ct)
                    .ConfigureAwait(false);
            }
            catch { /* best-effort */ }

            if (frames[0].SourcePath is { } path && frames[0].Line is int line)
                snippet = TryReadSnippet(path, line);
        }

        return new ExceptionAutopsy(
            threadId.ToString(), exceptionId, description, breakMode, layers, frames, topLocals, snippet);
    }

    private static void CollectLayers(JsonElement details, List<ExceptionLayer> layers)
    {
        var typeName = ReadString(details, "fullTypeName") ?? ReadString(details, "typeName");
        var message = ReadString(details, "message");
        var source = ReadString(details, "source");
        layers.Add(new ExceptionLayer(typeName, message, source));

        if (details.TryGetProperty("innerException", out var inner)
            && inner.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in inner.EnumerateArray())
                if (i.ValueKind == JsonValueKind.Object) CollectLayers(i, layers);
        }
    }

    // ---- source snippet ------------------------------------------------------------

    /// Best-effort: read a few lines around the highlighted line and return them
    /// in a printable form. Returns null if the file cannot be read.
    public static SourceSnippet? TryReadSnippet(string path, int highlightLine, int radius = 3)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            int start = Math.Max(1, highlightLine - radius);
            int end = Math.Min(lines.Length, highlightLine + radius);
            var sb = new StringBuilder();
            for (int i = start; i <= end; i++)
            {
                var marker = i == highlightLine ? "→ " : "  ";
                sb.Append(marker).Append(i.ToString().PadLeft(4)).Append(": ").AppendLine(lines[i - 1]);
            }
            return new SourceSnippet(path, start, end, highlightLine, sb.ToString());
        }
        catch
        {
            return null;
        }
    }

    // ---- helpers -------------------------------------------------------------------

    private static string? ReadString(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static int? ReadInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    }
}
