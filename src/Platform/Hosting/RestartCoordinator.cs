using System.Diagnostics;
using System.Runtime.Versioning;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// In-process restart-pending flag and helper-batch spawner. The flag is
/// set by every successful settings save; the dashboard polls it and shows
/// a global banner. <see cref="TriggerRestart"/> spawns a detached
/// <c>.cmd</c> that waits 2s, then sc-stops + sc-starts the service.
/// </summary>
/// <remarks>
/// Same helper-batch pattern as <see cref="UpgradeOrchestrator"/>: the
/// running .NET process can't restart itself synchronously, so we hand
/// the work to cmd.exe and exit when SCM tells us to.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class RestartCoordinator
{
    private static volatile bool _pending;
    private static DateTime? _since;
    private static readonly object _gate = new();

    public static bool IsPending => _pending;
    public static DateTime? PendingSince { get { lock (_gate) { return _since; } } }

    public static void MarkPending()
    {
        lock (_gate)
        {
            if (!_pending)
            {
                _pending = true;
                _since = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Writes the helper batch + spawns it detached. Returns immediately.
    /// The helper sleeps 2s (long enough for this process to flush its
    /// 202 response), then sc-stops and sc-starts the service.
    /// </summary>
    public static void TriggerRestart(ILogger logger)
    {
        var helperPath = Path.Combine(PlatformPaths.InstallDir, "restart-helper.cmd");
        var helperContent =
            "@echo off\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"sc stop {PlatformPaths.ServiceName} >nul 2>&1\r\n" +
            ":wait_exit\r\n" +
            "tasklist /fi \"imagename eq WinMCP.exe\" 2>nul | find /i \"WinMCP.exe\" >nul\r\n" +
            "if errorlevel 1 goto :start\r\n" +
            "timeout /t 1 /nobreak >nul\r\n" +
            "goto :wait_exit\r\n" +
            ":start\r\n" +
            $"sc start {PlatformPaths.ServiceName} >nul 2>&1\r\n" +
            "exit /b 0\r\n";
        File.WriteAllText(helperPath, helperContent);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{helperPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        using var p = Process.Start(psi);
        logger.LogInformation(
            "Restart helper spawned (PID {Pid}). Service stop will follow in ~2s.",
            p?.Id);
    }
}
