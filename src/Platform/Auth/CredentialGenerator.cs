using System.Security.Cryptography;

namespace WinMcp.Platform.Auth;

/// <summary>
/// Generates random credential strings for the demo auth mode. Format is
/// <c>{prefix}{base64url-of-N-random-bytes}</c>. Prefixes are public — they
/// identify which kind of credential this is (static bearer, issued OAuth
/// token, OAuth client id, OAuth client secret) so the auth middleware can
/// branch on shape before doing expensive validation.
/// </summary>
public static class CredentialGenerator
{
    /// <summary>Prefix for static bearer tokens (long-lived API key).</summary>
    public const string StaticBearerPrefix = "mm_st_";

    /// <summary>Prefix for OAuth2-issued access tokens (short-lived, in-memory).</summary>
    public const string IssuedTokenPrefix = "mm_at_";

    /// <summary>Prefix for OAuth2 client_id values.</summary>
    public const string ClientIdPrefix = "mm_cid_";

    /// <summary>Prefix for OAuth2 client_secret values.</summary>
    public const string ClientSecretPrefix = "mm_cs_";

    /// <summary>
    /// Returns a new credential string of the form <c>{prefix}{base64url(N random bytes)}</c>.
    /// </summary>
    public static string NewSecret(int byteLen, string prefix)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLen);
        return prefix + Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
