using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using AspNetCoreDebuggerMcp.Debugging;
using AspNetCoreDebuggerMcp.Tools;

namespace AspNetCoreDebuggerMcp.Tests.Integration;

/// End-to-end test exercising the same production code path the MCP tools use
/// (DebugSession.LaunchAsync → AddLineBreakpoint → WaitForStop → Continue) against
/// a real ASP.NET Core Web API target debugged through the *bundled* netcoredbg.
///
/// Skips when bundled binaries haven't been staged via scripts/fetch-netcoredbg-binaries.sh.
[Trait("Category", "Integration")]
public class BundledNetcoredbgEndToEndTests
{
    [Fact]
    public async Task DebugSession_LaunchesWebApi_HitsBreakpointOnRequest()
    {
        var bundled = LocateBundledNetcoredbg();
        if (bundled is null)
        {
            // No bundled binary for this RID — most likely CI host without the fetch
            // step, or local dev who hasn't run scripts/fetch-netcoredbg-binaries.sh.
            // Skip rather than fail.
            return;
        }

        var webApiDll = LocateBuiltAssembly("SampleWebApi");
        var programCs = LocateSourceFile("SampleWebApi", "Program.cs");
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        await using var session = await DebugSession.LaunchAsync(
            netcoredbgPath: bundled,
            program: webApiDll,
            args: new[] { "--urls", baseUrl },
            cwd: Path.GetDirectoryName(webApiDll),
            stopAtEntry: false,
            env: null,
            cts.Token);

        // Line 6 of Program.cs is inside the GET /users handler lambda body
        // (`var name = $"User{id}";`). Hit only when a request arrives.
        const int handlerLine = 6;
        await session.AddLineBreakpointAsync(
            sourcePath: programCs, line: handlerLine,
            condition: null, hitCondition: null, logMessage: null, cts.Token);

        await WaitForWebApiReadyAsync(baseUrl, cts.Token);

        // Fire the request that should hit the breakpoint. Don't await — the
        // request will hang while netcoredbg holds the debuggee paused at the BP.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var requestTask = http.GetAsync($"{baseUrl}/users/42", cts.Token);

        var stop = await session.WaitForStopAsync(TimeSpan.FromSeconds(20), cts.Token);
        Assert.NotNull(stop);
        Assert.Equal("breakpoint", stop.Reason);
        Assert.NotNull(stop.ThreadId);

        // Resume the debuggee so the request can complete.
        await session.ContinueAsync(stop.ThreadId, cts.Token);
        var response = await requestTask;
        Assert.True(response.IsSuccessStatusCode,
            $"Expected 2xx, got {(int)response.StatusCode}: {response.ReasonPhrase}");

        await session.DisconnectAsync(cts.Token);
    }

    [Fact]
    public async Task Manager_WaitForStop_ReturnsTopFrameLocalsAndRecentOutput()
    {
        var bundled = LocateBundledNetcoredbg();
        if (bundled is null) return;  // skip — see other test

        var webApiDll = LocateBuiltAssembly("SampleWebApi");
        var programCs = LocateSourceFile("SampleWebApi", "Program.cs");
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var manager = new DebugSessionManager();

        await manager.LaunchAsync(
            program: webApiDll,
            args: new[] { "--urls", baseUrl },
            cwd: Path.GetDirectoryName(webApiDll),
            stopAtEntry: false,
            env: null,
            cts.Token);

        const int handlerLine = 6;
        await manager.AddLineBreakpointAsync(
            sourcePath: programCs, line: handlerLine,
            condition: null, hitCondition: null, logMessage: null, cts.Token);

        await WaitForWebApiReadyAsync(baseUrl, cts.Token);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var requestTask = http.GetAsync($"{baseUrl}/users/42", cts.Token);

        var result = await manager.WaitForStopAsync(
            timeout: TimeSpan.FromSeconds(20),
            maxLocalsPerScope: 30,
            maxRecentOutputLines: 50,
            cts.Token);

        Assert.Equal("breakpoint", result.Stop.Reason);
        Assert.NotNull(result.TopFrame);

        // The new enriched fields — the whole point of this test.
        Assert.NotNull(result.TopFrameLocals);
        var allVars = result.TopFrameLocals!.SelectMany(s => s.Variables).ToList();
        Assert.Contains(allVars, v => v.Name == "id" && v.Value == "42");

        // RecentOutput is non-null whenever the cap is > 0. ASP.NET Core's startup
        // banner ("Now listening on…" / "Application started.") almost always lands
        // in the buffer before the BP hits, but it's racy — assert structure, not count.
        Assert.NotNull(result.RecentOutput);

        await manager.ContinueAsync(result.Stop.ThreadId, cts.Token);
        var response = await requestTask;
        Assert.True(response.IsSuccessStatusCode);

        await manager.DisconnectAsync(cts.Token);
    }

    [Fact]
    public async Task Tool_DebugStep_DefaultReturnsSimpleShape_NoEnrichment()
    {
        // Regression guard: when called without waitTimeoutSeconds, debug_step must
        // return ONLY { success, state } — the same shape it returned before this PR.
        // After issuing the no-wait step we still need a WaitForStop to let the step
        // settle before resuming, because manager.StepAsync only awaits the DAP ack,
        // not the resulting stop event — that's the exact "old pattern" agents use today.

        var bundled = LocateBundledNetcoredbg();
        if (bundled is null) return;

        var webApiDll = LocateBuiltAssembly("SampleWebApi");
        var programCs = LocateSourceFile("SampleWebApi", "Program.cs");
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var manager = new DebugSessionManager();
        var execTools = new ExecutionTools(manager);

        await manager.LaunchAsync(
            program: webApiDll, args: new[] { "--urls", baseUrl },
            cwd: Path.GetDirectoryName(webApiDll), stopAtEntry: false, env: null, cts.Token);

        const int handlerLine = 6;
        await manager.AddLineBreakpointAsync(
            sourcePath: programCs, line: handlerLine,
            condition: null, hitCondition: null, logMessage: null, cts.Token);

        await WaitForWebApiReadyAsync(baseUrl, cts.Token);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var requestTask = http.GetAsync($"{baseUrl}/users/42", cts.Token);

        var stop = await manager.WaitForStopAsync(
            timeout: TimeSpan.FromSeconds(20),
            maxLocalsPerScope: 30, maxRecentOutputLines: 50, cts.Token);
        Assert.Equal("breakpoint", stop.Stop.Reason);

        // Default (no wait): tool returns ONLY success + state. No enrichment fields.
        var defaultJson = await execTools.StepAsync(
            kind: "over", threadId: stop.Stop.ThreadId,
            waitTimeoutSeconds: null, maxLocalsPerScope: null, maxRecentOutputLines: null,
            ct: cts.Token);
        using (var doc = JsonDocument.Parse(defaultJson))
        {
            var root = doc.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.True(root.TryGetProperty("state", out _));
            Assert.False(root.TryGetProperty("topFrame", out _),
                "default debug_step (no wait) must not include enrichment fields");
            Assert.False(root.TryGetProperty("topFrameLocals", out _));
            Assert.False(root.TryGetProperty("recentOutput", out _));
            Assert.False(root.TryGetProperty("stop", out _));
        }

        // Settle the step (this is the agent flow today: step, then wait separately),
        // then resume so the request can complete.
        var postStep = await manager.WaitForStopAsync(
            timeout: TimeSpan.FromSeconds(20),
            maxLocalsPerScope: 0, maxRecentOutputLines: 0, cts.Token);
        Assert.Equal("step", postStep.Stop.Reason);

        await manager.ContinueAsync(postStep.Stop.ThreadId, cts.Token);
        var response = await requestTask;
        Assert.True(response.IsSuccessStatusCode);
        await manager.DisconnectAsync(cts.Token);
    }

    [Fact]
    public async Task Tool_DebugStep_OptInWaitReturnsEnrichedSnapshot()
    {
        // The headline feature: pass waitTimeoutSeconds and debug_step returns the same
        // one-shot snapshot as breakpoint_wait (stop, topFrame, snippet, locals, recentOutput)
        // — one tool call instead of two.

        var bundled = LocateBundledNetcoredbg();
        if (bundled is null) return;

        var webApiDll = LocateBuiltAssembly("SampleWebApi");
        var programCs = LocateSourceFile("SampleWebApi", "Program.cs");
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var manager = new DebugSessionManager();
        var execTools = new ExecutionTools(manager);

        await manager.LaunchAsync(
            program: webApiDll, args: new[] { "--urls", baseUrl },
            cwd: Path.GetDirectoryName(webApiDll), stopAtEntry: false, env: null, cts.Token);

        const int handlerLine = 6;
        await manager.AddLineBreakpointAsync(
            sourcePath: programCs, line: handlerLine,
            condition: null, hitCondition: null, logMessage: null, cts.Token);

        await WaitForWebApiReadyAsync(baseUrl, cts.Token);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var requestTask = http.GetAsync($"{baseUrl}/users/42", cts.Token);

        var stop = await manager.WaitForStopAsync(
            timeout: TimeSpan.FromSeconds(20),
            maxLocalsPerScope: 30, maxRecentOutputLines: 50, cts.Token);
        Assert.Equal("breakpoint", stop.Stop.Reason);

        var enrichedJson = await execTools.StepAsync(
            kind: "over", threadId: stop.Stop.ThreadId,
            waitTimeoutSeconds: 20, maxLocalsPerScope: 30, maxRecentOutputLines: 50,
            ct: cts.Token);

        int postStepThreadId;
        using (var doc = JsonDocument.Parse(enrichedJson))
        {
            var root = doc.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(),
                $"opt-in step+wait failed: {enrichedJson}");

            Assert.True(root.TryGetProperty("stop", out var stopEl));
            Assert.Equal("step", stopEl.GetProperty("reason").GetString());
            Assert.True(root.TryGetProperty("topFrame", out _));
            Assert.True(root.TryGetProperty("topFrameLocals", out var localsEl));
            Assert.True(root.TryGetProperty("recentOutput", out _));

            // `id` should still be in scope after a step inside the lambda body.
            var hasId = false;
            foreach (var scope in localsEl.EnumerateArray())
            foreach (var v in scope.GetProperty("variables").EnumerateArray())
            {
                if (v.GetProperty("name").GetString() == "id"
                    && v.GetProperty("value").GetString() == "42")
                {
                    hasId = true;
                }
            }
            Assert.True(hasId, "post-step top frame should still expose id=42");
            postStepThreadId = stopEl.GetProperty("threadId").GetInt32();
        }

        await manager.ContinueAsync(postStepThreadId, cts.Token);
        var response = await requestTask;
        Assert.True(response.IsSuccessStatusCode);
        await manager.DisconnectAsync(cts.Token);
    }

    [Fact]
    public async Task Tool_ExceptionAutopsy_IncludesRecentOutputField()
    {
        var bundled = LocateBundledNetcoredbg();
        if (bundled is null) return;

        var webApiDll = LocateBuiltAssembly("SampleWebApi");
        var programCs = LocateSourceFile("SampleWebApi", "Program.cs");
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var manager = new DebugSessionManager();
        var inspectTools = new InspectionTools(manager);

        await manager.LaunchAsync(
            program: webApiDll, args: new[] { "--urls", baseUrl },
            cwd: Path.GetDirectoryName(webApiDll), stopAtEntry: false, env: null, cts.Token);

        const int handlerLine = 6;
        await manager.AddLineBreakpointAsync(
            sourcePath: programCs, line: handlerLine,
            condition: null, hitCondition: null, logMessage: null, cts.Token);

        await WaitForWebApiReadyAsync(baseUrl, cts.Token);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var requestTask = http.GetAsync($"{baseUrl}/users/42", cts.Token);

        var stop = await manager.WaitForStopAsync(
            timeout: TimeSpan.FromSeconds(20),
            maxLocalsPerScope: 30, maxRecentOutputLines: 50, cts.Token);
        Assert.Equal("breakpoint", stop.Stop.Reason);

        // Autopsy doesn't require an exception stop — exceptionInfo is best-effort
        // and the rest (frames, locals, snippet) populates on any stop. That's enough
        // to verify the new recentOutput field is wired through.
        var autopsyJson = await inspectTools.AutopsyAsync(
            threadId: stop.Stop.ThreadId,
            frameCount: 5,
            maxRecentOutputLines: 50,
            ct: cts.Token);

        using (var doc = JsonDocument.Parse(autopsyJson))
        {
            var root = doc.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(),
                $"autopsy failed: {autopsyJson}");
            Assert.True(root.TryGetProperty("autopsy", out _));
            Assert.True(root.TryGetProperty("recentOutput", out var recentEl),
                "autopsy response must include recentOutput field");
            Assert.Equal(JsonValueKind.Array, recentEl.ValueKind);
        }

        // Verify the opt-out path: maxRecentOutputLines = 0 sets the field to null,
        // and the shared JSON serializer (WhenWritingNull) drops it from the wire.
        var optOutJson = await inspectTools.AutopsyAsync(
            threadId: stop.Stop.ThreadId,
            frameCount: 5,
            maxRecentOutputLines: 0,
            ct: cts.Token);
        using (var doc = JsonDocument.Parse(optOutJson))
        {
            var root = doc.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.False(root.TryGetProperty("recentOutput", out _),
                "opt-out (maxRecentOutputLines=0) must omit recentOutput from the response");
        }

        await manager.ContinueAsync(stop.Stop.ThreadId, cts.Token);
        var response = await requestTask;
        Assert.True(response.IsSuccessStatusCode);
        await manager.DisconnectAsync(cts.Token);
    }

    [Fact]
    public async Task DebugSession_PassesEnvironmentVariables_ToDebuggee()
    {
        var bundled = LocateBundledNetcoredbg();
        if (bundled is null) return;  // skip — see other test

        var webApiDll = LocateBuiltAssembly("SampleWebApi");
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        await using var session = await DebugSession.LaunchAsync(
            netcoredbgPath: bundled,
            program: webApiDll,
            args: new[] { "--urls", baseUrl },
            cwd: Path.GetDirectoryName(webApiDll),
            stopAtEntry: false,
            env: new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["SAMPLE_VAR"] = "hello-from-test",
            },
            cts.Token);

        await WaitForWebApiReadyAsync(baseUrl, cts.Token);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var json = await http.GetStringAsync($"{baseUrl}/health", cts.Token);

        // /health echoes the env var and ASP.NET Core hosting environment, so a successful
        // probe proves the env dict reached netcoredbg → debuggee process.
        Assert.Contains("\"environment\":\"Production\"", json);
        Assert.Contains("\"sampleVar\":\"hello-from-test\"", json);

        await session.DisconnectAsync(cts.Token);
    }

    // ---- helpers ------------------------------------------------------------

    /// Walks up from the test bin dir to find a bundled netcoredbg matching the
    /// current host RID. Returns null when not staged.
    private static string? LocateBundledNetcoredbg()
    {
        var rid = NetcoredbgLocator.CurrentRid();
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "netcoredbg.exe" : "netcoredbg";

        var repoRoot = FindRepoRoot();
        if (repoRoot is null) return null;

        var srcBin = Path.Combine(repoRoot, "src", "AspNetCoreDebuggerMcp", "bin");
        if (!Directory.Exists(srcBin)) return null;

        foreach (var configDir in Directory.EnumerateDirectories(srcBin))
        foreach (var tfmDir in Directory.EnumerateDirectories(configDir))
        {
            var candidate = Path.Combine(tfmDir, "runtimes", rid, "native", exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string LocateBuiltAssembly(string projectName)
    {
        var repoRoot = FindRepoRoot()
            ?? throw new InvalidOperationException("Repo root not found from test bin dir");
        var fixtureBin = Path.Combine(repoRoot, "tests", "fixtures", projectName, "bin");
        if (!Directory.Exists(fixtureBin))
            throw new FileNotFoundException(
                $"{projectName} not built. Expected at {fixtureBin}");

        foreach (var configDir in Directory.EnumerateDirectories(fixtureBin))
        foreach (var tfmDir in Directory.EnumerateDirectories(configDir))
        {
            var dll = Path.Combine(tfmDir, $"{projectName}.dll");
            if (File.Exists(dll)) return dll;
        }
        throw new FileNotFoundException(
            $"{projectName}.dll not found under any config/tfm in {fixtureBin}");
    }

    private static string LocateSourceFile(string projectName, string fileName)
    {
        var repoRoot = FindRepoRoot()
            ?? throw new InvalidOperationException("Repo root not found from test bin dir");
        var path = Path.Combine(repoRoot, "tests", "fixtures", projectName, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Expected source file at {path}");
        return path;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AspNetCoreDebuggerMcp.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "SPEC.md")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static async Task WaitForWebApiReadyAsync(string baseUrl, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var resp = await http.GetAsync($"{baseUrl}/health", ct);
                if (resp.IsSuccessStatusCode) return;
            }
            catch
            {
                // not listening yet — retry
            }
            await Task.Delay(250, ct);
        }
        throw new TimeoutException($"Web API at {baseUrl} did not become ready in 30s");
    }
}
