using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AspNetCoreDebuggerMcp.Debugging;
using ModelContextProtocol.Server;
using static AspNetCoreDebuggerMcp.Tools.ToolResults;

namespace AspNetCoreDebuggerMcp.Tools;

[McpServerToolType]
public sealed class HealthTools
{
    [McpServerTool(Name = "debugger_health")]
    [Description("Self-diagnostic. Reports the host platform, RID, resolved netcoredbg path " +
        "(or that it's missing), where the binary came from (bundled / env var / PATH), and the " +
        "netcoredbg version it reports. Call this first when something looks wrong, instead of " +
        "starting a real debug session to find out.")]
    public Task<string> DebuggerHealthAsync(CancellationToken ct = default)
    {
        var rid = NetcoredbgLocator.CurrentRid();
        var loc = NetcoredbgLocator.TryLocate();

        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
               : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
               : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
               : "unknown";
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var envVarValue = Environment.GetEnvironmentVariable(NetcoredbgLocator.EnvironmentVariable);

        if (loc is null)
        {
            var report = new
            {
                status = "binaryNotFound",
                rid,
                platform = new { os, architecture = arch },
                envVar = new
                {
                    name = NetcoredbgLocator.EnvironmentVariable,
                    set = !string.IsNullOrWhiteSpace(envVarValue),
                    value = envVarValue,
                },
                hint = $"No netcoredbg bundled for RID '{rid}' and none on PATH. Set " +
                       $"{NetcoredbgLocator.EnvironmentVariable} to a binary you've built or downloaded.",
            };
            return Task.FromResult(Serialize(report));
        }

        var version = TryReadVersion(loc.Path, ct);

        var ok = new
        {
            status = "ok",
            rid,
            platform = new { os, architecture = arch },
            netcoredbg = new
            {
                path = loc.Path,
                source = loc.Source.ToString(),  // EnvironmentVariable | Bundled | OnPath
                version,                           // null if --version couldn't be read
            },
            envVar = new
            {
                name = NetcoredbgLocator.EnvironmentVariable,
                set = !string.IsNullOrWhiteSpace(envVarValue),
                value = envVarValue,
            },
        };
        return Task.FromResult(Serialize(ok));
    }

    private static string? TryReadVersion(string netcoredbgPath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = netcoredbgPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            // Bound the wait so a hung binary doesn't hang the tool call.
            if (!proc.WaitForExit(2_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return null;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            // First non-empty line is the version banner (e.g. "NET Core debugger 3.1.3-1 (...)").
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (trimmed.Length > 0) return trimmed;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
