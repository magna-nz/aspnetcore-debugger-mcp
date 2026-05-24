using AspNetCoreDebuggerMcp.Debugging;

namespace AspNetCoreDebuggerMcp.Tests.Debugging;

/// Fast unit tests for the DAP launch-args dictionary that DebugSession sends.
/// The integration test (BundledNetcoredbgEndToEndTests) covers the full
/// end-to-end env-var flow; these guard the mapping itself.
public class DebugSessionLaunchArgsTests
{
    [Fact]
    public void BuildLaunchArgs_OmitsEnvKey_WhenEnvIsNull()
    {
        var d = DebugSession.BuildLaunchArgs(
            program: "/app/MyApi.dll",
            args: null, cwd: null, stopAtEntry: false, env: null);

        Assert.False(d.ContainsKey("env"));
    }

    [Fact]
    public void BuildLaunchArgs_OmitsEnvKey_WhenEnvIsEmpty()
    {
        var d = DebugSession.BuildLaunchArgs(
            program: "/app/MyApi.dll",
            args: null, cwd: null, stopAtEntry: false,
            env: new Dictionary<string, string>());

        Assert.False(d.ContainsKey("env"));
    }

    [Fact]
    public void BuildLaunchArgs_IncludesEnvKey_AsDictionary_WhenEnvIsPopulated()
    {
        var d = DebugSession.BuildLaunchArgs(
            program: "/app/MyApi.dll",
            args: null, cwd: null, stopAtEntry: false,
            env: new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["ConnectionStrings__Default"] = "Server=staging",
            });

        var env = Assert.IsAssignableFrom<IDictionary<string, object?>>(d["env"]);
        Assert.Equal("Production", env["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("Server=staging", env["ConnectionStrings__Default"]);
    }

    [Fact]
    public void BuildLaunchArgs_PreservesExistingFields()
    {
        var d = DebugSession.BuildLaunchArgs(
            program: "/app/MyApi.dll",
            args: new[] { "--urls", "http://127.0.0.1:5099" },
            cwd: "/app",
            stopAtEntry: true,
            env: new Dictionary<string, string> { ["FOO"] = "bar" });

        Assert.Equal("/app/MyApi.dll", d["program"]);
        Assert.Equal(true, d["stopAtEntry"]);
        Assert.Equal(true, d["justMyCode"]);
        Assert.Equal("/app", d["cwd"]);
        Assert.NotNull(d["args"]);
        Assert.NotNull(d["env"]);
    }
}
