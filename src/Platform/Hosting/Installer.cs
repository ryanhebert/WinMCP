using System.Runtime.Versioning;
using WinMcp.ModuleSdk;
using WinMcp.Platform.Auth;
using WinMcp.Platform.Config;
using WinMcp.Platform.Tls;

namespace WinMcp.Platform.Hosting;

public enum AuthFlag { NotSpecified, EnableDemo, ForceDisabled }

/// <summary>
/// Orchestrates install / uninstall / rotate-creds. Carries forward the
/// math-mcp v1.0.x installer flow with renames to WinMCP service/install dir
/// and an extra step for the modules subdirectory.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Installer
{
    public static int Install(int? httpPort, int? httpsPort, AuthFlag authFlag)
    {
        if (!ServiceControl.IsAdmin())
        {
            Console.Error.WriteLine("WinMCP install requires administrator privileges.");
            return 2;
        }

        Console.WriteLine("==> WinMCP install");

        // Stop + remove any prior service so the install is idempotent.
        StopAndRemoveExistingService();

        // Sweep stray foreground / upgrade-helper processes.
        var killed = ProcessSweep.KillStrayProcesses();
        if (killed > 0) Console.WriteLine($"    Killed {killed} stray WinMCP processes");

        // Create the install dir tree.
        Directory.CreateDirectory(PlatformPaths.InstallDir);
        Directory.CreateDirectory(PlatformPaths.LogDir);
        Directory.CreateDirectory(PlatformPaths.ModulesDir);
        Directory.CreateDirectory(PlatformPaths.CertDir);

        // Copy ourselves into Program Files.
        CopySelfToInstallDir();

        // Generate or refresh the self-signed cert.
        var certResult = CertificateProvider.EnsureCert(PlatformPaths.CertPath);
        Console.WriteLine($"    Cert: {certResult}");

        // Resolve config: load existing if present (preserve operator edits),
        // otherwise write defaults. Apply CLI flags as overrides.
        var configExisted = File.Exists(PlatformPaths.ConfigPath);
        var config = configExisted
            ? ConfigLoader.Load(PlatformPaths.ConfigPath)
            : ConfigLoader.DefaultsForFreshInstall();

        if (httpPort.HasValue) config.HttpPort = httpPort.Value;
        if (httpsPort.HasValue) config.HttpsPort = httpsPort.Value;

        ApplyAuthFlag(config, authFlag, configExisted);
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);
        Console.WriteLine($"    Config: {(configExisted ? "preserved" : "default")} → {PlatformPaths.ConfigPath}");

        // Firewall rules.
        FirewallSetup.Add(PlatformPaths.FirewallRuleHttp, config.HttpPort);
        FirewallSetup.Add(PlatformPaths.FirewallRuleHttps, config.HttpsPort);
        Console.WriteLine($"    Firewall: TCP/{config.HttpPort} + TCP/{config.HttpsPort} allowed inbound");

        // Register and start the service.
        RegisterAndStartService();

        Console.WriteLine();
        Console.WriteLine($"==> WinMCP installed and running.");
        Console.WriteLine($"    Dashboard: http://localhost:{config.HttpPort}/");
        Console.WriteLine($"    HTTPS:     https://localhost:{config.HttpsPort}/");

        return 0;
    }

    public static int Uninstall()
    {
        if (!ServiceControl.IsAdmin())
        {
            Console.Error.WriteLine("WinMCP uninstall requires administrator privileges.");
            return 2;
        }

        Console.WriteLine("==> WinMCP uninstall");
        StopAndRemoveExistingService();
        ProcessSweep.KillStrayProcesses();

        FirewallSetup.Remove(PlatformPaths.FirewallRuleHttp);
        FirewallSetup.Remove(PlatformPaths.FirewallRuleHttps);
        FirewallSetup.Remove(PlatformPaths.FirewallRulePort80);
        Console.WriteLine("    Firewall rules removed");

        try
        {
            if (Directory.Exists(PlatformPaths.InstallDir))
            {
                Directory.Delete(PlatformPaths.InstallDir, recursive: true);
                Console.WriteLine($"    Removed {PlatformPaths.InstallDir}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    Could not fully remove install dir: {ex.Message}");
            Console.Error.WriteLine("    (Run uninstall from outside the install dir; close any open files.)");
        }

        Console.WriteLine("==> Done.");
        return 0;
    }

    public static int RotateCreds()
    {
        if (!ServiceControl.IsAdmin())
        {
            Console.Error.WriteLine("WinMCP rotate-creds requires administrator privileges.");
            return 2;
        }

        if (!File.Exists(PlatformPaths.ConfigPath))
        {
            Console.Error.WriteLine($"No config at {PlatformPaths.ConfigPath}; nothing to rotate.");
            return 1;
        }

        var config = ConfigLoader.Load(PlatformPaths.ConfigPath);
        if (config.Mcp.DefaultAuth.Mode != AuthMode.Demo)
        {
            Console.Error.WriteLine("rotate-creds is only meaningful when mcp.defaultAuth.mode == demo.");
            return 1;
        }

        var fresh = DemoCredentials.Generate();
        config.Mcp.DefaultAuth = new AuthDomainConfig
        {
            Mode = AuthMode.Demo,
            DemoCredentials = fresh,
        };
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);

        Console.WriteLine("==> New demo credentials:");
        Console.WriteLine($"    bearer_token:  {fresh.BearerToken}");
        Console.WriteLine($"    client_id:     {fresh.ClientId}");
        Console.WriteLine($"    client_secret: {fresh.ClientSecret}");

        // Restart the service so the new creds take effect.
        ServiceControl.SafeRunSc("stop", PlatformPaths.ServiceName);
        ServiceControl.WaitForServiceState("STOPPED", 20);
        ServiceControl.SafeRunSc("start", PlatformPaths.ServiceName);

        return 0;
    }

    private static void ApplyAuthFlag(PlatformConfig config, AuthFlag flag, bool configExisted)
    {
        switch (flag)
        {
            case AuthFlag.EnableDemo:
                // Only generate fresh creds if we don't already have them.
                if (config.Mcp.DefaultAuth.Mode != AuthMode.Demo ||
                    config.Mcp.DefaultAuth.DemoCredentials is null)
                {
                    config.Mcp.DefaultAuth = new AuthDomainConfig
                    {
                        Mode = AuthMode.Demo,
                        DemoCredentials = DemoCredentials.Generate(),
                    };
                    Console.WriteLine("    Auth: demo mode (fresh credentials generated)");
                }
                else
                {
                    Console.WriteLine("    Auth: demo mode (existing credentials preserved)");
                }
                break;

            case AuthFlag.ForceDisabled:
                config.Mcp.DefaultAuth = new AuthDomainConfig { Mode = AuthMode.None };
                Console.WriteLine("    Auth: disabled (--auth off)");
                break;

            case AuthFlag.NotSpecified:
                if (configExisted && config.Mcp.DefaultAuth.Mode == AuthMode.Demo)
                {
                    Console.Error.WriteLine(
                        "Existing install has auth enabled but --auth was not specified.");
                    Console.Error.WriteLine(
                        "  Re-run with --auth to keep demo auth, or --auth off to disable it.");
                    Environment.Exit(2);
                }
                // Otherwise no change.
                break;
        }
    }

    private static void StopAndRemoveExistingService()
    {
        var (queryExit, _, _) = ServiceControl.RunSc("query", PlatformPaths.ServiceName);
        if (queryExit != 0) return;

        ServiceControl.SafeRunSc("stop", PlatformPaths.ServiceName);
        ServiceControl.WaitForServiceState("STOPPED", 20);
        ServiceControl.SafeRunSc("delete", PlatformPaths.ServiceName);

        if (!ServiceControl.WaitForServiceGone(30))
        {
            Console.Error.WriteLine(
                "Existing WinMcp service is stuck 'marked for deletion'. " +
                "Close services.msc (and any process holding a handle to it) and retry.");
            Environment.Exit(1);
        }
    }

    private static void CopySelfToInstallDir()
    {
        var current = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
        var target = PlatformPaths.InstalledExePath;

        // If we're already running from the install dir, no copy needed.
        if (PathsEqual(current, target)) return;

        File.Copy(current, target, overwrite: true);

        // Strip Mark-of-the-Web on the copied exe so Windows doesn't nag.
        var zoneStream = target + ":Zone.Identifier";
        try { File.Delete(zoneStream); } catch { /* best-effort */ }
    }

    private static void RegisterAndStartService()
    {
        ServiceControl.SafeRunSc(
            "create", PlatformPaths.ServiceName,
            "binPath=", $"\"{PlatformPaths.InstalledExePath}\"",
            "start=", "auto",
            "obj=", PlatformPaths.ServiceAccount,
            "DisplayName=", PlatformPaths.ServiceDisplayName);
        ServiceControl.SafeRunSc(
            "description", PlatformPaths.ServiceName, PlatformPaths.ServiceDescription);
        ServiceControl.SafeRunSc(
            "failure", PlatformPaths.ServiceName, "reset=", "60", "actions=", "restart/5000/restart/5000/restart/5000");

        ServiceControl.GrantServiceSelfControl();

        ServiceControl.SafeRunSc("start", PlatformPaths.ServiceName);
        if (!ServiceControl.WaitForServiceState("RUNNING", 30))
        {
            Console.Error.WriteLine("Service did not reach RUNNING within 30s.");
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
