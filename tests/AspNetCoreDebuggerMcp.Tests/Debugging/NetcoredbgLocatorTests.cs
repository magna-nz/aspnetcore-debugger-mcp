using AspNetCoreDebuggerMcp.Debugging;

namespace AspNetCoreDebuggerMcp.Tests.Debugging;

public class NetcoredbgLocatorTests
{
    [Fact]
    public void EnvVar_Wins_WhenSetAndFileExists()
    {
        var result = NetcoredbgLocator.LocateCore(
            envPath: "/custom/netcoredbg",
            baseDirectory: "/tool",
            rid: "osx-arm64",
            isWindows: false,
            pathEnv: "/usr/bin",
            fileExists: p => p == "/custom/netcoredbg" || p == "/tool/runtimes/osx-arm64/native/netcoredbg");

        Assert.Equal("/custom/netcoredbg", result);
    }

    [Fact]
    public void EnvVar_Ignored_WhenFileMissing()
    {
        var result = NetcoredbgLocator.LocateCore(
            envPath: "/nope/netcoredbg",
            baseDirectory: "/tool",
            rid: "osx-arm64",
            isWindows: false,
            pathEnv: "",
            fileExists: p => p == "/tool/runtimes/osx-arm64/native/netcoredbg");

        Assert.Equal("/tool/runtimes/osx-arm64/native/netcoredbg", result);
    }

    [Fact]
    public void BundledBinary_ResolvedFromRid()
    {
        var result = NetcoredbgLocator.LocateCore(
            envPath: null,
            baseDirectory: "/tool",
            rid: "linux-x64",
            isWindows: false,
            pathEnv: "",
            fileExists: p => p == "/tool/runtimes/linux-x64/native/netcoredbg");

        Assert.Equal("/tool/runtimes/linux-x64/native/netcoredbg", result);
    }

    [Fact]
    public void BundledBinary_UsesExeOnWindows()
    {
        var expected = Path.Combine(@"C:\tool", "runtimes", "win-x64", "native", "netcoredbg.exe");

        var result = NetcoredbgLocator.LocateCore(
            envPath: null,
            baseDirectory: @"C:\tool",
            rid: "win-x64",
            isWindows: true,
            pathEnv: "",
            fileExists: p => p == expected);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FallsBack_ToPath_WhenBundledMissing()
    {
        var result = NetcoredbgLocator.LocateCore(
            envPath: null,
            baseDirectory: "/tool",
            rid: "osx-arm64",
            isWindows: false,
            pathEnv: "/usr/local/bin:/usr/bin",
            fileExists: p => p == "/usr/local/bin/netcoredbg");

        Assert.Equal("/usr/local/bin/netcoredbg", result);
    }

    [Fact]
    public void Throws_WhenNothingFound()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            NetcoredbgLocator.LocateCore(
                envPath: null,
                baseDirectory: "/tool",
                rid: "osx-arm64",
                isWindows: false,
                pathEnv: "/usr/bin",
                fileExists: _ => false));

        Assert.Contains("osx-arm64", ex.Message);
        Assert.Contains(NetcoredbgLocator.EnvironmentVariable, ex.Message);
    }

    [Fact]
    public void CurrentRid_ReturnsOsAndArch()
    {
        var rid = NetcoredbgLocator.CurrentRid();
        Assert.Matches(@"^(win|osx|linux)-(x64|arm64)$", rid);
    }
}
