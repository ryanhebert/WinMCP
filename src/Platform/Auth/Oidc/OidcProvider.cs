namespace WinMcp.Platform.Auth.Oidc;

/// <summary>
/// One configured OIDC identity provider. The operator declares the
/// <see cref="Issuer"/> URL only; everything else is auto-discovered from
/// <c>{Issuer}/.well-known/openid-configuration</c> at startup and cached
/// here. Both the admin and MCP auth domains may reference the same
/// provider by <see cref="Name"/>.
/// </summary>
public sealed class OidcProvider
{
    /// <summary>Name used to reference this provider from auth configs (e.g., "corp-okta").</summary>
    public required string Name { get; init; }

    /// <summary>Issuer URL declared by the operator. Must match the <c>iss</c> claim of issued tokens.</summary>
    public required string Issuer { get; init; }

    /// <summary>Discovered <c>jwks_uri</c> from the provider's metadata.</summary>
    public string? JwksUrl { get; set; }

    /// <summary>Discovered authorization endpoint (informational; v1.1 uses for admin-mode login redirect).</summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>Discovered token endpoint (informational).</summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>Audience to enforce on token <c>aud</c> claim. Defaults to "winmcp" if not set; operators can override per provider.</summary>
    public string Audience { get; set; } = "winmcp";

    /// <summary>UTC timestamp of the last successful discovery refresh.</summary>
    public DateTime DiscoveredAtUtc { get; set; }
}
