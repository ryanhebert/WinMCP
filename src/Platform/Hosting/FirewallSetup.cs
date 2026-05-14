using System.Runtime.Versioning;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// Manages Windows Firewall inbound rules for the platform's listening ports.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FirewallSetup
{
    public static void Add(string ruleName, int port)
    {
        // Delete any existing rule of the same name first so re-installs end
        // up idempotent (no duplicate rules accumulating).
        ServiceControl.Run("netsh.exe", new[]
        {
            "advfirewall", "firewall", "delete", "rule",
            $"name={ruleName}"
        });

        ServiceControl.Run("netsh.exe", new[]
        {
            "advfirewall", "firewall", "add", "rule",
            $"name={ruleName}",
            "dir=in", "action=allow", "protocol=TCP",
            $"localport={port}"
        });
    }

    public static void Remove(string ruleName)
    {
        ServiceControl.Run("netsh.exe", new[]
        {
            "advfirewall", "firewall", "delete", "rule",
            $"name={ruleName}"
        });
    }
}
