using System.Runtime.InteropServices;

namespace AspNetCoreDebuggerMcp.Debugging;

/// Finds the netcoredbg executable on disk.
/// Resolution order:
///   1. NETCOREDBG_PATH env var
///   2. Bundled binary at runtimes/&lt;rid&gt;/native/netcoredbg[.exe]
///   3. On PATH
internal static class NetcoredbgLocator
{
    public const string EnvironmentVariable = "NETCOREDBG_PATH";

    public static string Locate() =>
        LocateCore(
            envPath: Environment.GetEnvironmentVariable(EnvironmentVariable),
            baseDirectory: AppContext.BaseDirectory,
            rid: CurrentRid(),
            isWindows: RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            pathEnv: Environment.GetEnvironmentVariable("PATH") ?? "",
            fileExists: File.Exists);

    internal static string LocateCore(
        string? envPath,
        string baseDirectory,
        string rid,
        bool isWindows,
        string pathEnv,
        Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(envPath) && fileExists(envPath))
            return envPath;

        var exe = isWindows ? "netcoredbg.exe" : "netcoredbg";
        var bundled = Path.Combine(baseDirectory, "runtimes", rid, "native", exe);
        if (fileExists(bundled))
            return bundled;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, exe);
            if (fileExists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            $"netcoredbg executable not found. The package should bundle a binary for RID '{rid}' " +
            $"at runtimes/{rid}/native/{exe}. Set the {EnvironmentVariable} environment variable to " +
            "override, or install netcoredbg on PATH.");
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
}
