namespace AspNetCoreDebuggerMcp.Debugging;

/// Finds the netcoredbg executable on disk.
/// Resolution order: NETCOREDBG_PATH env var → bundled-next-to-assembly → on PATH.
internal static class NetcoredbgLocator
{
    public const string EnvironmentVariable = "NETCOREDBG_PATH";

    public static string Locate()
    {
        var envPath = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
        {
            Path.Combine(baseDir, "netcoredbg", "netcoredbg"),
            Path.Combine(baseDir, "netcoredbg"),
        })
        {
            if (File.Exists(candidate)) return candidate;
        }

        var onPath = FindOnPath("netcoredbg");
        if (onPath is not null) return onPath;

        throw new FileNotFoundException(
            $"netcoredbg executable not found. Set the {EnvironmentVariable} environment variable " +
            "to the netcoredbg binary, or place it on PATH.");
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
