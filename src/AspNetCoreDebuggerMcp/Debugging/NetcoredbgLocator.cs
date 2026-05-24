using System.Runtime.InteropServices;

namespace AspNetCoreDebuggerMcp.Debugging;

/// Where a resolved netcoredbg binary came from. `debugger_health` surfaces
/// this so an agent can distinguish "you set the env var" from "we used
/// the bundled binary" without rerunning resolution.
internal enum NetcoredbgSource
{
    EnvironmentVariable,
    Bundled,
    OnPath,
}

internal sealed record NetcoredbgLocation(string Path, NetcoredbgSource Source);

/// Finds the netcoredbg executable on disk.
/// Resolution order:
///   1. NETCOREDBG_PATH env var
///   2. Bundled binary at runtimes/&lt;rid&gt;/native/netcoredbg[.exe]
///   3. On PATH
internal static class NetcoredbgLocator
{
    public const string EnvironmentVariable = "NETCOREDBG_PATH";

    public static string Locate()
    {
        var loc = TryLocate();
        if (loc is null) throw NotFound(CurrentRid(), IsWindows());
        return loc.Path;
    }

    public static NetcoredbgLocation? TryLocate() =>
        TryLocateCore(
            envPath: Environment.GetEnvironmentVariable(EnvironmentVariable),
            baseDirectory: AppContext.BaseDirectory,
            rid: CurrentRid(),
            isWindows: IsWindows(),
            pathEnv: Environment.GetEnvironmentVariable("PATH") ?? "",
            fileExists: File.Exists);

    internal static NetcoredbgLocation? TryLocateCore(
        string? envPath,
        string baseDirectory,
        string rid,
        bool isWindows,
        string pathEnv,
        Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(envPath) && fileExists(envPath))
            return new NetcoredbgLocation(envPath, NetcoredbgSource.EnvironmentVariable);

        var exe = isWindows ? "netcoredbg.exe" : "netcoredbg";
        var bundled = Path.Combine(baseDirectory, "runtimes", rid, "native", exe);
        if (fileExists(bundled))
            return new NetcoredbgLocation(bundled, NetcoredbgSource.Bundled);

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, exe);
            if (fileExists(candidate))
                return new NetcoredbgLocation(candidate, NetcoredbgSource.OnPath);
        }

        return null;
    }

    /// Path-only variant kept for the existing tests that predate `TryLocateCore`.
    internal static string LocateCore(
        string? envPath, string baseDirectory, string rid,
        bool isWindows, string pathEnv, Func<string, bool> fileExists)
    {
        var loc = TryLocateCore(envPath, baseDirectory, rid, isWindows, pathEnv, fileExists);
        if (loc is null) throw NotFound(rid, isWindows);
        return loc.Path;
    }

    internal static string CurrentRid()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
               : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
               : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
               : throw new PlatformNotSupportedException(
                   $"Unsupported OS: {RuntimeInformation.OSDescription}");

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            var a => throw new PlatformNotSupportedException(
                $"Unsupported process architecture: {a}"),
        };

        return $"{os}-{arch}";
    }

    private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static FileNotFoundException NotFound(string rid, bool isWindows)
    {
        var exe = isWindows ? "netcoredbg.exe" : "netcoredbg";
        return new FileNotFoundException(
            $"netcoredbg executable not found. The package should bundle a binary for RID '{rid}' " +
            $"at runtimes/{rid}/native/{exe}. Set the {EnvironmentVariable} environment variable to " +
            "override, or install netcoredbg on PATH.");
    }
}
