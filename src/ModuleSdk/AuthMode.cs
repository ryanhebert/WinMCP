namespace WinMcp.ModuleSdk;

/// <summary>
/// The platform's authentication mode for a given auth domain.
/// </summary>
/// <remarks>
/// WinMCP has two independent auth domains: the admin/operator surface
/// (dashboard, /info, /upgrade, etc.) and the MCP module surface (per-module
/// /mcp endpoints). Each domain selects one of these modes via configuration.
/// </remarks>
public enum AuthMode
{
    /// <summary>No authentication. Anyone with network reachability can hit the endpoint.</summary>
    None,

    /// <summary>
    /// Platform-issued credentials (static bearer + OAuth2 client_credentials).
    /// Credentials are published on the dashboard; intended for integrators
    /// testing MCP auth flows, not for protecting real data. Only valid on the
    /// MCP domain — the admin domain does not support demo mode.
    /// </summary>
    Demo,

    /// <summary>
    /// JWTs validated against a configured external OIDC provider. Standard
    /// production auth mode. Requires <c>oidcProviders</c> to be configured
    /// in the platform config and referenced by name.
    /// </summary>
    Oidc
}
