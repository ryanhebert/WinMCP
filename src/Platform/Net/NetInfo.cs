using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WinMcp.Platform.Net;

/// <summary>
/// Network identity + reachability helpers. Ported from math-mcp v1.0.24
/// unchanged except for namespace.
/// </summary>
internal static class NetInfo
{
    /// <summary>
    /// Tries to bind <paramref name="port"/> briefly to confirm it's free.
    /// Retries up to <paramref name="retries"/> times with
    /// <paramref name="delayMs"/> between attempts so a TIME_WAIT'd port
    /// from a recent service stop (typical after an upgrade) has a chance
    /// to clear. Default 5 × 1 s = up to 5 s tolerance.
    /// </summary>
    public static bool TryProbeFreePort(int port, int retries = 5, int delayMs = 1000)
    {
        for (var attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Any, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch
            {
                if (attempt < retries - 1) Thread.Sleep(delayMs);
            }
        }
        return false;
    }

    public static string ResolveFqdn()
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var host = string.IsNullOrEmpty(props.HostName) ? Environment.MachineName : props.HostName;
            var domain = props.DomainName ?? "";
            if (string.IsNullOrEmpty(domain)) return host;
            if (host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) return host;
            return $"{host}.{domain}";
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    public static string HttpUrl(string host, int port, string path) =>
        port == 80 ? $"http://{host}{path}" : $"http://{host}:{port}{path}";

    public static string HttpsUrl(string host, int port, string path) =>
        port == 443 ? $"https://{host}{path}" : $"https://{host}:{port}{path}";
}
