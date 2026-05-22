using System.Diagnostics;

namespace AspNetCoreDebuggerMcp.Debugging;

/// Owns a child netcoredbg process running in DAP (vscode) interpreter mode.
/// Exposes the input/output streams that a DAP client reads from / writes to.
internal sealed class NetcoredbgProcess : IAsyncDisposable
{
    private readonly Process _process;

    public Stream Input => _process.StandardOutput.BaseStream;   // we READ netcoredbg's stdout
    public Stream Output => _process.StandardInput.BaseStream;   // we WRITE to netcoredbg's stdin

    private NetcoredbgProcess(Process process) => _process = process;

    public static NetcoredbgProcess Start(string netcoredbgPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = netcoredbgPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--interpreter=vscode");

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start netcoredbg at {netcoredbgPath}");
        return new NetcoredbgProcess(process);
    }

    public bool HasExited => _process.HasExited;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // dispose is best-effort
        }
        finally
        {
            _process.Dispose();
        }
    }
}
