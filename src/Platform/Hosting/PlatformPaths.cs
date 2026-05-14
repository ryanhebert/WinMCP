namespace WinMcp.Platform.Hosting;

/// <summary>
/// Canonical filesystem paths and identifiers for the installed platform.
/// One place to change if the install dir or service name ever rebrands.
/// </summary>
public static class PlatformPaths
{
    public const string ServiceName = "WinMcp";
    public const string ServiceDisplayName = "WinMCP Platform";
    public const string ServiceDescription =
        "WinMCP — multi-module MCP (Model Context Protocol) server platform.";
    public const string ServiceAccount = @"NT SERVICE\WinMcp";
    public const string FirewallRuleHttp = "WinMCP HTTP";
    public const string FirewallRuleHttps = "WinMCP HTTPS";
    public const string FirewallRulePort80 = "WinMCP HTTP (port 80, token only)";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinMCP");

    public static string InstalledExePath => Path.Combine(InstallDir, "WinMCP.exe");
    public static string ConfigPath => Path.Combine(InstallDir, "config.json");
    public static string CertDir => Path.Combine(InstallDir, "certs");
    public static string CertPath => Path.Combine(CertDir, "cert.pfx");
    public static string LogDir => Path.Combine(InstallDir, "logs");
    public static string ModulesDir => Path.Combine(InstallDir, "modules");

    public static string UpgradeStagingExe => Path.Combine(InstallDir, "WinMCP.exe.new");
    public static string UpgradeHelperBat => Path.Combine(InstallDir, "upgrade-helper.cmd");
    public static string UpgradeFailedMarker => Path.Combine(InstallDir, "upgrade-failed.txt");

    public static string ModuleDir(string name) => Path.Combine(ModulesDir, name);
    public static string ModuleStagingDir(string name) => Path.Combine(ModulesDir, name + ".staging");
    public static string ModuleStagingZip(string name) => Path.Combine(InstallDir, $"module-{name}.zip");
    public static string ModuleUpgradeHelperBat => Path.Combine(InstallDir, "module-upgrade-helper.cmd");
    public static string ModuleUpgradeFailedMarker(string name) =>
        Path.Combine(InstallDir, $"module-upgrade-{name}-failed.txt");
}
