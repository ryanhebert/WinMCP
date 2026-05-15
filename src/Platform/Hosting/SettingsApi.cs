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
        app.MapPost("/api/settings/oidc-providers", (Delegate)HandleAddProvider);
        app.MapPut("/api/settings/oidc-providers/{name}", (Delegate)HandleUpdateProvider);
        app.MapDelete("/api/settings/oidc-providers/{name}", (Delegate)HandleDeleteProvider);
        app.MapPost("/api/settings/oidc-providers/{name}/rediscover", (Delegate)HandleRediscover);
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

    // ----- OIDC Provider CRUD handlers -----

    internal sealed record AddProviderRequest(string Name, string Issuer, string? Audience);

    private static async Task<IResult> HandleAddProvider(HttpContext ctx)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        AddProviderRequest? body;
        try
        {
            body = await ctx.Request.ReadFromJsonAsync<AddProviderRequest>();
        }
        catch
        {
            return Results.Json(new { error = "invalid_request", message = "malformed JSON body" }, statusCode: 400);
        }
        if (body is null)
        {
            return Results.Json(new { error = "invalid_request", message = "body is required" }, statusCode: 400);
        }

        var audience = string.IsNullOrWhiteSpace(body.Audience) ? "winmcp" : body.Audience;
        var validation = SettingsValidator.ValidateProviderShape(body.Name, body.Issuer, audience);
        if (!validation.Ok)
        {
            return Results.Json(new { error = "invalid_request", field = validation.Field, message = validation.Message }, statusCode: 400);
        }

        if (config.OidcProviders.ContainsKey(body.Name))
        {
            return Results.Json(new { error = "already_exists", message = $"provider '{body.Name}' already exists" }, statusCode: 409);
        }

        // Discover synchronously — design doc requires this.
        var http = ctx.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        var disco = new Auth.Oidc.OidcDiscovery(http,
            ctx.RequestServices.GetRequiredService<ILogger<Auth.Oidc.OidcDiscovery>>());
        var probe = new Auth.Oidc.OidcProvider { Name = body.Name, Issuer = body.Issuer, Audience = audience };
        try
        {
            await disco.DiscoverAsync(probe);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OIDC discovery failed for proposed provider {Name} issuer={Issuer}", body.Name, body.Issuer);
            return Results.Json(new { error = "discovery_failed", message = ex.Message }, statusCode: 422);
        }

        var entry = new OidcProviderConfig
        {
            Issuer = body.Issuer,
            Audience = audience,
            JwksUrl = probe.JwksUrl,
            AuthorizationEndpoint = probe.AuthorizationEndpoint,
            TokenEndpoint = probe.TokenEndpoint,
            DiscoveredAtIso = DateTime.UtcNow.ToString("O"),
        };
        config.OidcProviders[body.Name] = entry;
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);

        // Register live so subsequent admin/mcp save calls' providerRef
        // resolution sees the new provider without requiring a restart.
        ctx.RequestServices.GetRequiredService<Auth.Oidc.OidcProviderRegistry>().Upsert(probe);

        RestartCoordinator.MarkPending();
        logger.LogWarning("Added OIDC provider name={Name} issuer={Issuer}", body.Name, body.Issuer);
        return Results.Json(ToProviderDto(body.Name, entry, config), statusCode: 201);
    }

    internal sealed record UpdateProviderRequest(string Issuer, string? Audience);

    private static async Task<IResult> HandleUpdateProvider(HttpContext ctx, string name)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        if (!config.OidcProviders.TryGetValue(name, out var existing))
        {
            return Results.NotFound(new { error = "not_found", message = $"provider '{name}' does not exist" });
        }

        UpdateProviderRequest? body;
        try { body = await ctx.Request.ReadFromJsonAsync<UpdateProviderRequest>(); }
        catch { return Results.Json(new { error = "invalid_request", message = "malformed JSON body" }, statusCode: 400); }
        if (body is null) return Results.Json(new { error = "invalid_request", message = "body is required" }, statusCode: 400);

        var audience = string.IsNullOrWhiteSpace(body.Audience) ? "winmcp" : body.Audience;
        var validation = SettingsValidator.ValidateProviderShape(name, body.Issuer, audience);
        if (!validation.Ok)
        {
            return Results.Json(new { error = "invalid_request", field = validation.Field, message = validation.Message }, statusCode: 400);
        }

        // Issuer change → must re-discover. Audience-only change → keep existing endpoints.
        var issuerChanged = !string.Equals(body.Issuer, existing.Issuer, StringComparison.Ordinal);
        var entry = new OidcProviderConfig
        {
            Issuer = body.Issuer,
            Audience = audience,
            JwksUrl = issuerChanged ? null : existing.JwksUrl,
            AuthorizationEndpoint = issuerChanged ? null : existing.AuthorizationEndpoint,
            TokenEndpoint = issuerChanged ? null : existing.TokenEndpoint,
            DiscoveredAtIso = issuerChanged ? null : existing.DiscoveredAtIso,
        };

        if (issuerChanged)
        {
            var http = ctx.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
            var disco = new Auth.Oidc.OidcDiscovery(http,
                ctx.RequestServices.GetRequiredService<ILogger<Auth.Oidc.OidcDiscovery>>());
            var probe = new Auth.Oidc.OidcProvider { Name = name, Issuer = body.Issuer, Audience = audience };
            try { await disco.DiscoverAsync(probe); }
            catch (Exception ex)
            {
                return Results.Json(new { error = "discovery_failed", message = ex.Message }, statusCode: 422);
            }
            entry.JwksUrl = probe.JwksUrl;
            entry.AuthorizationEndpoint = probe.AuthorizationEndpoint;
            entry.TokenEndpoint = probe.TokenEndpoint;
            entry.DiscoveredAtIso = DateTime.UtcNow.ToString("O");
            ctx.RequestServices.GetRequiredService<Auth.Oidc.OidcProviderRegistry>().Upsert(probe);
        }

        config.OidcProviders[name] = entry;
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);
        RestartCoordinator.MarkPending();
        logger.LogWarning("Updated OIDC provider name={Name}", name);
        return Results.Json(ToProviderDto(name, entry, config));
    }

    private static IResult HandleDeleteProvider(HttpContext ctx, string name)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        if (!config.OidcProviders.ContainsKey(name))
        {
            return Results.NotFound(new { error = "not_found", message = $"provider '{name}' does not exist" });
        }

        var validation = SettingsValidator.ValidateProviderDeletable(name, config);
        if (!validation.Ok)
        {
            return Results.Json(new
            {
                error = "in_use",
                message = validation.Message,
                inUseBy = validation.InUseBy ?? Array.Empty<string>(),
            }, statusCode: 409);
        }

        config.OidcProviders.Remove(name);
        ctx.RequestServices.GetRequiredService<Auth.Oidc.OidcProviderRegistry>().Remove(name);
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);
        RestartCoordinator.MarkPending();
        logger.LogWarning("Deleted OIDC provider name={Name}", name);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleRediscover(HttpContext ctx, string name)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        if (!config.OidcProviders.TryGetValue(name, out var existing))
        {
            return Results.NotFound(new { error = "not_found", message = $"provider '{name}' does not exist" });
        }

        var http = ctx.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        var disco = new Auth.Oidc.OidcDiscovery(http,
            ctx.RequestServices.GetRequiredService<ILogger<Auth.Oidc.OidcDiscovery>>());
        var probe = new Auth.Oidc.OidcProvider
        {
            Name = name,
            Issuer = existing.Issuer,
            Audience = existing.Audience ?? "winmcp",
        };
        try { await disco.DiscoverAsync(probe); }
        catch (Exception ex)
        {
            return Results.Json(new { error = "discovery_failed", message = ex.Message }, statusCode: 422);
        }

        existing.JwksUrl = probe.JwksUrl;
        existing.AuthorizationEndpoint = probe.AuthorizationEndpoint;
        existing.TokenEndpoint = probe.TokenEndpoint;
        existing.DiscoveredAtIso = DateTime.UtcNow.ToString("O");
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);
        ctx.RequestServices.GetRequiredService<Auth.Oidc.OidcProviderRegistry>().Upsert(probe);
        // Re-discovery doesn't flip the restart-pending flag; no auth-mode
        // change occurred, just refreshed endpoint URLs.
        logger.LogInformation("Re-discovered OIDC provider name={Name}", name);
        return Results.Json(ToProviderDto(name, existing, config));
    }
}
