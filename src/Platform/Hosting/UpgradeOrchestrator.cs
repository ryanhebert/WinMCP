using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// In-place platform upgrade pipeline. Ported from math-mcp v1.0.24.
/// <para>
/// <c>POST /upgrade</c> downloads a newer <c>WinMCP.exe</c> from GitHub,
/// PE-verifies it, writes a small helper batch, spawns it as a detached
/// process, and returns 202 immediately. The helper waits for the service
/// process to exit, moves the new binary over the running one (with retry
/// to ride out antivirus locks), and asks SCM to start the service back.
/// </para>
/// <para>
/// <c>GET /upgrade/status</c> reports the in-process state machine so the
/// dashboard can render progress. Once the service restart succeeds, the
/// fresh process comes up with state back at <c>idle</c>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UpgradeOrchestrator
{
    private static int _upgradeInFlight;
    private static readonly UpgradeStatus _status = new();

    /// <summary>
    /// Mutable shared state for the GET endpoint. Mutated only from the
    /// background <see cref="Task.Run"/> in the upgrade handler; readers can
    /// see an occasional torn read (e.g., "downloading" with a stale bytes
    /// counter) and that's fine — polling smooths over it.
    /// </summary>
    public sealed class UpgradeStatus
    {
        public string State { get; set; } = "idle";   // idle | downloading | staged | restarting | done | failed
        public string? Message { get; set; }
        public string? TargetVersion { get; set; }
        public string? StartedAtIso { get; set; }
        public long? BytesDownloaded { get; set; }
        public long? BytesTotal { get; set; }
    }

    public static void MapEndpoints(WebApplication app)
    {
        // Cast to Delegate so the framework binds HandleUpgradePost as a
        // typed route handler (returning IResult) instead of a raw
        // RequestDelegate, which would discard the response object.
        app.MapPost("/upgrade", (Delegate)HandleUpgradePost);

        // Live progress for the in-UI upgrade. Reported state transitions:
        //   idle → downloading → staged → restarting → (process exits)
        // After the new service comes up, this returns to idle.
        app.MapGet("/upgrade/status", () => Results.Json(new
        {
            state = _status.State,
            message = _status.Message,
            target_version = _status.TargetVersion,
            started_at = _status.StartedAtIso,
            bytes_downloaded = _status.BytesDownloaded,
            bytes_total = _status.BytesTotal,
        }));
    }

    private static async Task<IResult> HandleUpgradePost(HttpContext ctx)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Upgrade");

        if (Interlocked.CompareExchange(ref _upgradeInFlight, 1, 0) != 0)
        {
            logger.LogWarning(
                "Upgrade rejected: already in progress ip={Ip}",
                ctx.Connection.RemoteIpAddress);
            return Results.Json(
                new { status = "error", error = "upgrade_in_progress" },
                statusCode: 409);
        }

        string version = "latest";
        try
        {
            if (ctx.Request.HasJsonContentType())
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                if (doc.RootElement.TryGetProperty("version", out var v) &&
                    v.ValueKind == JsonValueKind.String)
                {
                    version = v.GetString() ?? "latest";
                }
            }
        }
        catch { /* default to latest */ }

        // Only accept "latest" or version tags shaped like v1.2.3[.4]. This
        // shapes what we'll splice into the download URL — anything else and
        // we'd be at risk of attacker-controlled paths.
        if (version != "latest" &&
            !Regex.IsMatch(version, @"^v\d+\.\d+\.\d+(\.\d+)?$"))
        {
            Interlocked.Exchange(ref _upgradeInFlight, 0);
            return Results.Json(new { status = "error", error = "invalid_version" }, statusCode: 400);
        }

        var downloadUrl = version == "latest"
            ? "https://github.com/ryanhebert/WinMCP/releases/latest/download/WinMCP.exe"
            : $"https://github.com/ryanhebert/WinMCP/releases/download/{version}/WinMCP-{version}.exe";

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
        logger.LogWarning("Upgrade requested: target={Version} ip={Ip}", version, ip);

        _status.State = "downloading";
        _status.Message = null;
        _status.TargetVersion = version;
        _status.StartedAtIso = DateTime.UtcNow.ToString("O");
        _status.BytesDownloaded = 0;
        _status.BytesTotal = null;

        _ = Task.Run(() => RunUpgradePipeline(version, downloadUrl, logger));

        return Results.Json(new
        {
            status = "initiated",
            target_version = version,
            note = "Poll /upgrade/status for progress; /info for the new version.",
        }, statusCode: 202);
    }

    private static async Task RunUpgradePipeline(string version, string downloadUrl, ILogger logger)
    {
        var newExePath = PlatformPaths.UpgradeStagingExe;
        var helperPath = PlatformPaths.UpgradeHelperBat;
        var installedExePath = PlatformPaths.InstalledExePath;
        var failMarker = PlatformPaths.UpgradeFailedMarker;

        var clearLock = true;
        try
        {
            // Pre-clean any leftover staging artefacts from a previous attempt
            // — overwriting protects against a half-written file.
            TryDelete(newExePath);
            TryDelete(failMarker);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var asmVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"WinMCP-Upgrade/{asmVersion}");

            logger.LogInformation("Downloading {Url}", downloadUrl);

            using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            _status.BytesTotal = response.Content.Headers.ContentLength;

            long downloaded = 0;
            using (var fileStream = File.Create(newExePath))
            using (var netStream = await response.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await netStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    _status.BytesDownloaded = downloaded;
                }
            }

            // Verify the downloaded file: size + PE "MZ" magic. Anything
            // smaller than 1 MB or missing the header is rejected.
            var fileInfo = new FileInfo(newExePath);
            if (fileInfo.Length < 1_000_000)
            {
                _status.State = "failed";
                _status.Message = $"downloaded file too small ({fileInfo.Length} bytes)";
                logger.LogError("Downloaded artifact rejected: size={Size}", fileInfo.Length);
                TryDelete(newExePath);
                return;
            }
            using (var fs = File.OpenRead(newExePath))
            {
                var sig = new byte[2];
                fs.ReadExactly(sig);
                if (sig[0] != (byte)'M' || sig[1] != (byte)'Z')
                {
                    _status.State = "failed";
                    _status.Message = $"downloaded file is not a Windows executable (header={sig[0]:X2}{sig[1]:X2})";
                    logger.LogError("Downloaded artifact rejected: bad PE header {H0:X2}{H1:X2}", sig[0], sig[1]);
                    TryDelete(newExePath);
                    return;
                }
            }

            logger.LogInformation("Wrote {Bytes} bytes to {Path}", fileInfo.Length, newExePath);
            _status.State = "staged";

            // Helper batch: stops the service, waits for the .exe file to be
            // unlocked, retries the swap up to 10× (handles antivirus briefly
            // holding the file), then restarts. On unrecoverable swap failure,
            // writes a marker file and starts the OLD binary so the service
            // comes back up rather than dying silent.
            var helperContent =
                "@echo off\r\n" +
                "timeout /t 3 /nobreak >nul\r\n" +
                $"sc stop {PlatformPaths.ServiceName} >nul 2>&1\r\n" +
                ":wait_exit\r\n" +
                "tasklist /fi \"imagename eq WinMCP.exe\" 2>nul | find /i \"WinMCP.exe\" >nul\r\n" +
                "if errorlevel 1 goto :swap\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                "goto :wait_exit\r\n" +
                ":swap\r\n" +
                "set RETRY=0\r\n" +
                ":try_swap\r\n" +
                $"move /y \"{newExePath}\" \"{installedExePath}\" >nul 2>&1\r\n" +
                "if not errorlevel 1 goto :start\r\n" +
                "set /a RETRY+=1\r\n" +
                "if %RETRY% lss 10 (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                "  goto :try_swap\r\n" +
                ")\r\n" +
                $"echo Upgrade swap failed at %date% %time% (target={version})> \"{failMarker}\"\r\n" +
                ":start\r\n" +
                $"sc start {PlatformPaths.ServiceName} >nul 2>&1\r\n" +
                "exit /b 0\r\n";
            await File.WriteAllTextAsync(helperPath, helperContent);

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
                "Upgrade helper spawned (PID {Pid}). Service stop will follow in ~3s.",
                p?.Id);
            _status.State = "restarting";
            clearLock = false; // We got far enough; lock stays set until the process dies.

            // Watchdog: if we're still alive 5 minutes from now, the helper
            // either hung or failed silently. Release the in-flight lock so
            // future /upgrade calls aren't permanently blocked at 409, and
            // surface the stall in /upgrade/status.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                if (_status.State == "restarting")
                {
                    _status.State = "failed";
                    _status.Message = "helper did not restart the service within 5 minutes";
                    logger.LogError(
                        "Upgrade watchdog: helper did not restart the service within 5 minutes — releasing the in-flight lock");
                }
                Interlocked.Exchange(ref _upgradeInFlight, 0);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upgrade pipeline failed");
            _status.State = "failed";
            _status.Message = ex.Message;
        }
        finally
        {
            if (clearLock) Interlocked.Exchange(ref _upgradeInFlight, 0);
        }
    }

    /// <summary>
    /// At service startup, scrub any leftover upgrade-related files. These
    /// can persist when a prior upgrade attempt failed before the swap, or
    /// when the helper batch was canceled mid-flight. Surfaces a one-time
    /// WARN if an <c>upgrade-failed.txt</c> marker is present so operators
    /// know to investigate.
    /// </summary>
    public static void CleanupStaleArtefacts(ILogger logger)
    {
        var marker = PlatformPaths.UpgradeFailedMarker;
        if (File.Exists(marker))
        {
            try
            {
                var content = File.ReadAllText(marker).Trim();
                logger.LogWarning(
                    "Previous /upgrade attempt left a failure marker: {Content}. Old binary is still running.",
                    content);
            }
            catch { /* ignore */ }
            TryDelete(marker);
        }

        TryDelete(PlatformPaths.UpgradeStagingExe);
        TryDelete(PlatformPaths.UpgradeHelperBat);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
