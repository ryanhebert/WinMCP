using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinMcp.Platform.Modules;

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
    private static IReadOnlyList<LoadedModule> _loadedModules = Array.Empty<LoadedModule>();
    private static string _platformVersion = "0.0.0";

    /// <summary>
    /// Snapshot of host state needed by the module-upgrade handler — loaded
    /// modules (for updateSource + current version) and the running platform
    /// version (for minPlatformVersion compatibility checks against the
    /// staged module.json). Captured at startup; modules don't hot-swap, so
    /// a snapshot at MapEndpoints time is the same as live state until the
    /// service restarts.
    /// </summary>
    public static void Configure(IReadOnlyList<LoadedModule> loaded, string platformVersion)
    {
        _loadedModules = loaded;
        _platformVersion = platformVersion;
    }

    /// <summary>
    /// Mutable shared state for the GET endpoint. Mutated only from the
    /// background <see cref="Task.Run"/> in the upgrade handler; readers can
    /// see an occasional torn read (e.g., "downloading" with a stale bytes
    /// counter) and that's fine — polling smooths over it.
    /// </summary>
    public sealed class UpgradeStatus
    {
        public string State { get; set; } = "idle";   // idle | downloading | staged | restarting | done | failed
        public string Kind { get; set; } = "platform"; // platform | module
        public string? Module { get; set; }            // populated when Kind=module
        public string? Message { get; set; }
        public string? TargetVersion { get; set; }
        public string? StartedAtIso { get; set; }
        public long? BytesDownloaded { get; set; }
        public long? BytesTotal { get; set; }
    }

    public static void MapEndpoints(WebApplication app)
    {
        // Cast to Delegate so the framework binds these as typed route
        // handlers (returning IResult) instead of as raw RequestDelegates,
        // which would discard the response object.
        app.MapPost("/upgrade", (Delegate)HandleUpgradePost);
        app.MapPost("/upgrade/module/{name}", (Delegate)HandleModuleUpgradePost);

        // Live progress for the in-UI upgrade. Reported state transitions:
        //   idle → downloading → staged → restarting → (process exits)
        // After the new service comes up, this returns to idle. The Kind +
        // Module fields tell the dashboard which scope of upgrade is running.
        app.MapGet("/upgrade/status", () => Results.Json(new
        {
            state = _status.State,
            kind = _status.Kind,
            module = _status.Module,
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
        _status.Kind = "platform";
        _status.Module = null;
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

    private static async Task<IResult> HandleModuleUpgradePost(HttpContext ctx, string name)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Upgrade.Module");

        var loaded = _loadedModules.FirstOrDefault(m =>
            string.Equals(m.Name, name, StringComparison.Ordinal));
        if (loaded is null)
        {
            return Results.Json(new { status = "error", error = "module_not_found" }, statusCode: 404);
        }
        if (loaded.Manifest.UpdateSource is null)
        {
            return Results.Json(new
            {
                status = "error",
                error = "no_update_source",
                message = "this module's manifest does not declare an updateSource; upgrade manually by replacing the folder",
            }, statusCode: 400);
        }

        if (Interlocked.CompareExchange(ref _upgradeInFlight, 1, 0) != 0)
        {
            logger.LogWarning(
                "Module upgrade rejected: an upgrade is already in progress module={Module} ip={Ip}",
                name, ctx.Connection.RemoteIpAddress);
            return Results.Json(new { status = "error", error = "upgrade_in_progress" }, statusCode: 409);
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

        if (version != "latest" &&
            !Regex.IsMatch(version, @"^v\d+\.\d+\.\d+(\.\d+)?$"))
        {
            Interlocked.Exchange(ref _upgradeInFlight, 0);
            return Results.Json(new { status = "error", error = "invalid_version" }, statusCode: 400);
        }

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
        logger.LogWarning(
            "Module upgrade requested: module={Module} target={Version} ip={Ip}",
            name, version, ip);

        _status.State = "downloading";
        _status.Kind = "module";
        _status.Module = name;
        _status.Message = null;
        _status.TargetVersion = version;
        _status.StartedAtIso = DateTime.UtcNow.ToString("O");
        _status.BytesDownloaded = 0;
        _status.BytesTotal = null;

        _ = Task.Run(() => RunModuleUpgradePipeline(loaded, version, logger));

        return Results.Json(new
        {
            status = "initiated",
            module = name,
            target_version = version,
            note = "Poll /upgrade/status for progress; /info for the new version.",
        }, statusCode: 202);
    }

    private static async Task RunModuleUpgradePipeline(LoadedModule loaded, string requestedVersion, ILogger logger)
    {
        var source = loaded.Manifest.UpdateSource!;
        var moduleName = loaded.Name;
        var currentVersion = loaded.Manifest.Version;
        var stagingDir = PlatformPaths.ModuleStagingDir(moduleName);
        var moduleDir = PlatformPaths.ModuleDir(moduleName);
        var stagingZip = PlatformPaths.ModuleStagingZip(moduleName);
        var helperPath = PlatformPaths.ModuleUpgradeHelperBat;
        var failMarker = PlatformPaths.ModuleUpgradeFailedMarker(moduleName);

        var clearLock = true;
        try
        {
            // Pre-clean any leftover artefacts from a previous attempt.
            TryDeleteDir(stagingDir);
            TryDelete(stagingZip);
            TryDelete(failMarker);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var asmVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"WinMCP-ModuleUpgrade/{asmVersion}");

            // 1. Resolve the release tag. "latest" → GitHub API releases/latest.
            string tag;
            if (requestedVersion == "latest")
            {
                tag = await ResolveLatestTag(http, source.Repo, logger);
            }
            else
            {
                tag = requestedVersion;
            }

            // 2. Substitute placeholders in the asset template + build URL.
            //   {tag}      → the raw release tag (e.g. "math-v1.0.0")
            //   {version}  → the SemVer-with-v portion (e.g. "v1.0.0"),
            //                obtained by stripping a "<moduleName>-" prefix
            //                from the tag if present. For single-module
            //                repos with tags shaped like "v1.0.0" this is
            //                a no-op so the platform path still works.
            var version = tag.StartsWith(moduleName + "-", StringComparison.Ordinal)
                ? tag.Substring(moduleName.Length + 1)
                : tag;
            var assetName = source.Asset
                .Replace("{tag}", tag, StringComparison.Ordinal)
                .Replace("{version}", version, StringComparison.Ordinal);
            var downloadUrl = $"https://github.com/{source.Repo}/releases/download/{tag}/{assetName}";
            logger.LogInformation("Downloading module {Module} from {Url}", moduleName, downloadUrl);

            // 3. Stream the zip with byte-progress.
            using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                _status.BytesTotal = response.Content.Headers.ContentLength;

                long downloaded = 0;
                using var fileStream = File.Create(stagingZip);
                using var netStream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[81920];
                int read;
                while ((read = await netStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    _status.BytesDownloaded = downloaded;
                }
            }

            // 4. Verify zip magic (PK\x03\x04). Reject anything else.
            using (var fs = File.OpenRead(stagingZip))
            {
                var sig = new byte[4];
                fs.ReadExactly(sig);
                if (sig[0] != 'P' || sig[1] != 'K' || sig[2] != 0x03 || sig[3] != 0x04)
                {
                    _status.State = "failed";
                    _status.Message = $"downloaded file is not a zip archive (header={sig[0]:X2}{sig[1]:X2}{sig[2]:X2}{sig[3]:X2})";
                    logger.LogError("Downloaded module artifact rejected: bad zip header for {Module}", moduleName);
                    TryDelete(stagingZip);
                    return;
                }
            }

            // 5. Extract into staging dir.
            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(stagingZip, stagingDir, overwriteFiles: true);
            TryDelete(stagingZip);
            logger.LogInformation("Extracted module {Module} into {Dir}", moduleName, stagingDir);

            // 6. Validate the staged module.json.
            var stagedManifestPath = Path.Combine(stagingDir, "module.json");
            if (!File.Exists(stagedManifestPath))
            {
                _status.State = "failed";
                _status.Message = "staged archive is missing module.json";
                logger.LogError("Staged archive for module {Module} is missing module.json", moduleName);
                TryDeleteDir(stagingDir);
                return;
            }

            ModuleManifest staged;
            try
            {
                staged = ModuleManifestParser.ParseFile(stagedManifestPath);
            }
            catch (ModuleManifestException ex)
            {
                _status.State = "failed";
                _status.Message = $"staged module.json is invalid: {ex.Message}";
                logger.LogError(ex, "Staged manifest for module {Module} failed validation", moduleName);
                TryDeleteDir(stagingDir);
                return;
            }

            if (!string.Equals(staged.Name, moduleName, StringComparison.Ordinal))
            {
                _status.State = "failed";
                _status.Message = $"staged module.json declares name '{staged.Name}' but we're upgrading '{moduleName}'";
                logger.LogError(
                    "Staged manifest name mismatch: staged={Staged} expected={Expected}",
                    staged.Name, moduleName);
                TryDeleteDir(stagingDir);
                return;
            }

            if (CompareSemVer(staged.MinPlatformVersion, _platformVersion) > 0)
            {
                _status.State = "failed";
                _status.Message = $"staged module requires platform >= {staged.MinPlatformVersion} but we're running {_platformVersion}";
                logger.LogError(
                    "Staged manifest minPlatformVersion={Min} > running platform={Running}",
                    staged.MinPlatformVersion, _platformVersion);
                TryDeleteDir(stagingDir);
                return;
            }

            if (string.Equals(staged.Version, currentVersion, StringComparison.Ordinal))
            {
                _status.State = "done";
                _status.Message = $"already at version {currentVersion}";
                logger.LogInformation(
                    "Module {Module} already at version {Version}; no swap needed",
                    moduleName, currentVersion);
                TryDeleteDir(stagingDir);
                return;
            }

            logger.LogInformation(
                "Module {Module} swap pending: {Current} → {Staged}",
                moduleName, currentVersion, staged.Version);
            _status.State = "staged";

            // 7. Helper batch: stop service, wait for exit, remove the old
            // module dir, move staging into its place, restart. On swap
            // failure leave staging in place + write marker + start old
            // service back so the platform never dies silent.
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
                $"rmdir /s /q \"{moduleDir}\" >nul 2>&1\r\n" +
                $"move /y \"{stagingDir}\" \"{moduleDir}\" >nul 2>&1\r\n" +
                "if not errorlevel 1 goto :start\r\n" +
                "set /a RETRY+=1\r\n" +
                "if %RETRY% lss 10 (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                "  goto :try_swap\r\n" +
                ")\r\n" +
                $"echo Module upgrade swap failed at %date% %time% (module={moduleName}, target={tag})> \"{failMarker}\"\r\n" +
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
                "Module upgrade helper spawned (PID {Pid}) for module={Module}. Service stop will follow in ~3s.",
                p?.Id, moduleName);
            _status.State = "restarting";
            clearLock = false; // Process is about to die; lock stays set.

            // Watchdog: same 5-min cap as the platform path.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                if (_status.State == "restarting")
                {
                    _status.State = "failed";
                    _status.Message = "helper did not restart the service within 5 minutes";
                    logger.LogError(
                        "Module upgrade watchdog: helper did not restart the service within 5 minutes for module={Module}",
                        moduleName);
                }
                Interlocked.Exchange(ref _upgradeInFlight, 0);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Module upgrade pipeline failed for {Module}", loaded.Name);
            _status.State = "failed";
            _status.Message = ex.Message;
        }
        finally
        {
            if (clearLock) Interlocked.Exchange(ref _upgradeInFlight, 0);
        }
    }

    private static async Task<string> ResolveLatestTag(HttpClient http, string repo, ILogger logger)
    {
        var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
        var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString()
                  ?? throw new InvalidOperationException($"GitHub release for {repo} has no tag_name");
        logger.LogInformation("Resolved latest tag for {Repo} = {Tag}", repo, tag);
        return tag;
    }

    /// <summary>
    /// Lex-compares two dotted-numeric SemVer cores ignoring pre-release /
    /// build metadata. Sufficient for minPlatformVersion gating where we
    /// only care about "is the staged module compatible with the running
    /// platform"; full SemVer ordering isn't needed.
    /// </summary>
    private static int CompareSemVer(string a, string b)
    {
        var ap = a.Split('-', '+')[0].Split('.');
        var bp = b.Split('-', '+')[0].Split('.');
        var len = Math.Max(ap.Length, bp.Length);
        for (var i = 0; i < len; i++)
        {
            var av = i < ap.Length && int.TryParse(ap[i], out var aa) ? aa : 0;
            var bv = i < bp.Length && int.TryParse(bp[i], out var bb) ? bb : 0;
            if (av != bv) return av < bv ? -1 : 1;
        }
        return 0;
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
        TryDelete(PlatformPaths.ModuleUpgradeHelperBat);

        // Module-scope: surface failure markers, then scrub any leftover
        // <name>.staging dirs + zip files. A staging dir present at startup
        // means the helper either didn't run or finished without removing
        // it — either way, the running module is whatever's at <name>/, so
        // the staging copy is now ambiguous and unsafe to keep.
        if (Directory.Exists(PlatformPaths.InstallDir))
        {
            foreach (var f in Directory.EnumerateFiles(PlatformPaths.InstallDir, "module-upgrade-*-failed.txt"))
            {
                try
                {
                    var content = File.ReadAllText(f).Trim();
                    logger.LogWarning(
                        "Previous module /upgrade attempt left a failure marker: {Content}",
                        content);
                }
                catch { /* ignore */ }
                TryDelete(f);
            }
            foreach (var f in Directory.EnumerateFiles(PlatformPaths.InstallDir, "module-*.zip"))
            {
                TryDelete(f);
            }
        }

        if (Directory.Exists(PlatformPaths.ModulesDir))
        {
            foreach (var d in Directory.EnumerateDirectories(PlatformPaths.ModulesDir, "*.staging"))
            {
                logger.LogWarning(
                    "Scrubbing leftover module-upgrade staging dir: {Dir}",
                    d);
                TryDeleteDir(d);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort */ }
    }
}
