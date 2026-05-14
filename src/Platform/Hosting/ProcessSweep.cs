using System.Diagnostics;
using System.Runtime.Versioning;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// Sweeps stray <c>WinMCP.exe</c> processes prior to install or uninstall.
/// Carries forward math-mcp's v1.0.17 install-side sweep: catches debug
/// foregrounders, stuck upgrade helpers, etc.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessSweep
{
    public static int KillStrayProcesses()
    {
        var currentPid = Environment.ProcessId;
        var killed = 0;
        foreach (var p in Process.GetProcessesByName("WinMCP"))
        {
            try
            {
                if (p.Id == currentPid) { p.Dispose(); continue; }
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
                killed++;
            }
            catch { /* best-effort */ }
            finally { p.Dispose(); }
        }
        return killed;
    }
}
