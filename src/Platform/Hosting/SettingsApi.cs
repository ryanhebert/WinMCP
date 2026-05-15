using System.Runtime.Versioning;
using WinMcp.ModuleSdk;
using WinMcp.Platform.Auth;
using WinMcp.Platform.Config;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// REST handlers for <c>/api/settings/*</c>. Sibling of
/// <see cref="UpgradeOrchestrator"/>: a static class with handler methods
/// that mutate <c>config.json</c> + flip <see cref="RestartCoordinator"/>'s
/// pending flag. The endpoints are gated by the existing admin-auth
/// middleware registered in <see cref="RouteRegistration"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SettingsApi
{
    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/settings", (Delegate)HandleGetSnapshot);
        app.MapGet("/api/settings/restart-pending", (Delegate)HandleGetRestartPending);
    }

    // ----- DTOs -----

    internal sealed record SettingsSnapshotDto(
        AuthDomainDto Admin,
        AuthDomainDto Mcp,
        IReadOnlyList<OidcProviderDto> OidcProviders);

    internal sealed record AuthDomainDto(
        string Mode,                 // "none" | "demo" | "oidc"
        string? ProviderRef,
        IReadOnlyList<string> RequiredScopes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredClaims);

    internal sealed record OidcProviderDto(
        string Name,
        string Issuer,
        string? Audience,
        string? JwksUrl,
        string? AuthorizationEndpoint,
        string? TokenEndpoint,
        string? DiscoveredAtIso,
        IReadOnlyList<string> InUseBy);  // ["admin"], ["mcp"], or both

    internal sealed record RestartPendingDto(bool Pending, string? Since);

    // ----- Handlers -----

    private static IResult HandleGetSnapshot(HttpContext ctx)
    {
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();
        return Results.Json(ToSnapshot(config));
    }

    private static IResult HandleGetRestartPending() =>
        Results.Json(new RestartPendingDto(
            Pending: RestartCoordinator.IsPending,
            Since: RestartCoordinator.PendingSince?.ToString("O")));

    // ----- Helpers (also used by later handler tasks) -----

    internal static SettingsSnapshotDto ToSnapshot(PlatformConfig c) =>
        new(
            Admin: ToAuthDto(c.Admin.Auth),
            Mcp: ToAuthDto(c.Mcp.DefaultAuth),
            OidcProviders: c.OidcProviders.Select(kvp => ToProviderDto(kvp.Key, kvp.Value, c)).ToList());

    internal static AuthDomainDto ToAuthDto(AuthDomainConfig a) =>
        new(
            Mode: a.Mode.ToString().ToLowerInvariant(),
            ProviderRef: a.ProviderRef,
            RequiredScopes: a.RequiredScopes,
            RequiredClaims: a.RequiredClaims);

    internal static OidcProviderDto ToProviderDto(string name, OidcProviderConfig p, PlatformConfig c)
    {
        var inUseBy = new List<string>();
        if (c.Admin.Auth.Mode == AuthMode.Oidc &&
            string.Equals(c.Admin.Auth.ProviderRef, name, StringComparison.Ordinal))
        {
            inUseBy.Add("admin");
        }
        if (c.Mcp.DefaultAuth.Mode == AuthMode.Oidc &&
            string.Equals(c.Mcp.DefaultAuth.ProviderRef, name, StringComparison.Ordinal))
        {
            inUseBy.Add("mcp");
        }
        foreach (var (moduleName, settings) in c.Modules)
        {
            if (settings.AuthOverride is { Mode: AuthMode.Oidc } overrideAuth &&
                string.Equals(overrideAuth.ProviderRef, name, StringComparison.Ordinal))
            {
                inUseBy.Add($"module:{moduleName}");
            }
        }
        return new OidcProviderDto(
            Name: name,
            Issuer: p.Issuer,
            Audience: p.Audience,
            JwksUrl: p.JwksUrl,
            AuthorizationEndpoint: p.AuthorizationEndpoint,
            TokenEndpoint: p.TokenEndpoint,
            DiscoveredAtIso: p.DiscoveredAtIso,
            InUseBy: inUseBy);
    }
}
