using WinMcp.ModuleSdk;

namespace WinMcp.Platform.Auth;

/// <summary>
/// Configuration for a single auth domain (admin or MCP-default). Operators
/// edit one of these per domain in <c>config.json</c>. Modules may carry an
/// override of the same shape in their <c>module.json</c> or in the platform
/// config's per-module overrides.
/// </summary>
public sealed class AuthDomainConfig
{
    public AuthMode Mode { get; init; } = AuthMode.None;

    /// <summary>
    /// Name of an entry in <c>oidcProviders</c> when <see cref="Mode"/> is
    /// <see cref="AuthMode.Oidc"/>. Null otherwise.
    /// </summary>
    public string? ProviderRef { get; init; }

    /// <summary>
    /// Scopes required on incoming tokens when <see cref="Mode"/> is
    /// <see cref="AuthMode.Oidc"/>. Reserved for v1.1 — the v1.0
    /// <see cref="OidcValidator"/> stub doesn't consult them.
    /// </summary>
    public IReadOnlyList<string> RequiredScopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Required claim key/value(s) on incoming tokens. Same v1.1 reservation
    /// note as <see cref="RequiredScopes"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredClaims { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Demo-mode credentials when <see cref="Mode"/> is <see cref="AuthMode.Demo"/>.
    /// Generated at <c>--auth</c> install time; persisted in <c>config.json</c>.
    /// Null for other modes.
    /// </summary>
    public DemoCredentials? DemoCredentials { get; init; }
}
