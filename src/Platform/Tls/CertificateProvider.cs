using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinMcp.Platform.Net;

namespace WinMcp.Platform.Tls;

/// <summary>
/// Generates and loads the platform's self-signed TLS certificate. Ports
/// from math-mcp v1.0.24, preserving the v1.0.19 auto-renew-on-startup
/// behavior: <see cref="EnsureCert"/> inspects the existing pfx and
/// regenerates it when within 30 days of expiry.
/// </summary>
public static class CertificateProvider
{
    private const int KeySizeBits = 2048;
    private const int ValidityDays = 365;
    private const int RenewWithinDays = 30;
    private const string SubjectName = "CN=localhost";
    private const string SanDnsName = "localhost";

    public enum EnsureResult { AlreadyValid, Created, Renewed }

    public static EnsureResult EnsureCert(string pfxPath)
    {
        var existed = File.Exists(pfxPath);
        if (existed && !NeedsRenewal(pfxPath)) return EnsureResult.AlreadyValid;

        Directory.CreateDirectory(Path.GetDirectoryName(pfxPath)!);

        using var rsa = RSA.Create(KeySizeBits);
        var request = new CertificateRequest(
            SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(SanDnsName);

        // Also add the machine name and FQDN so clients connecting via those
        // hostnames don't get a TLS hostname-mismatch warning.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SanDnsName };
        TryAddSan(sanBuilder, seen, NetInfo.ResolveFqdn());
        TryAddSan(sanBuilder, seen, Environment.MachineName);

        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, // serverAuth
                critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddDays(ValidityDays);

        using var cert = request.CreateSelfSigned(notBefore, notAfter);
        var pfxBytes = cert.Export(X509ContentType.Pfx);
        File.WriteAllBytes(pfxPath, pfxBytes);
        return existed ? EnsureResult.Renewed : EnsureResult.Created;
    }

    /// <summary>
    /// Returns the existing cert's <c>NotAfter</c> as a string (best-effort,
    /// for log messages) or <c>"unreadable"</c> if the file can't be parsed.
    /// Uses <see cref="X509KeyStorageFlags.EphemeralKeySet"/> so we don't
    /// import the private key into the machine key store just to peek.
    /// </summary>
    public static string DescribeExpiry(string pfxPath)
    {
        try
        {
            using var cert = new X509Certificate2(
                pfxPath, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
            return cert.NotAfter.ToString("yyyy-MM-dd");
        }
        catch
        {
            return "unreadable";
        }
    }

    private static bool NeedsRenewal(string pfxPath)
    {
        try
        {
            using var cert = new X509Certificate2(
                pfxPath, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
            return cert.NotAfter <= DateTime.Now.AddDays(RenewWithinDays);
        }
        catch
        {
            return true;
        }
    }

    public static X509Certificate2 Load(string pfxPath) =>
        new(pfxPath, (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

    private static void TryAddSan(SubjectAlternativeNameBuilder b, HashSet<string> seen, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!seen.Add(name)) return;
        try { b.AddDnsName(name); }
        catch { /* silently skip invalid DNS labels */ }
    }
}
