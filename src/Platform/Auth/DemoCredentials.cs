namespace WinMcp.Platform.Auth;

/// <summary>
/// Public test credentials issued by the platform when MCP auth mode is
/// <c>demo</c>. Surfaced on the dashboard and in <c>/info</c>; not secret.
/// Carries forward the math-mcp convention (mm_st_ / mm_cid_ / mm_cs_
/// prefixes; default 1-hour TTL on issued OAuth tokens).
/// </summary>
public sealed class DemoCredentials
{
    /// <summary>Long-lived static bearer (mm_st_…). Issued once per install.</summary>
    public required string BearerToken { get; init; }

    /// <summary>OAuth2 client_credentials client_id (mm_cid_…).</summary>
    public required string ClientId { get; init; }

    /// <summary>OAuth2 client_credentials client_secret (mm_cs_…).</summary>
    public required string ClientSecret { get; init; }

    /// <summary>TTL applied to /token-issued bearers (mm_at_…). Default 3600 s.</summary>
    public int TokenTtlSeconds { get; init; } = 3600;

    /// <summary>Convenience factory for fresh-install credential generation.</summary>
    public static DemoCredentials Generate() => new()
    {
        BearerToken = CredentialGenerator.NewSecret(32, CredentialGenerator.StaticBearerPrefix),
        ClientId = CredentialGenerator.NewSecret(16, CredentialGenerator.ClientIdPrefix),
        ClientSecret = CredentialGenerator.NewSecret(32, CredentialGenerator.ClientSecretPrefix),
    };
}
