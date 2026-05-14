using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinMcp.ModuleSdk;
using WinMcp.Platform.Auth;
using WinMcp.Platform.Config;
using WinMcp.Platform.Modules;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// Wires HTTP routes onto a built <see cref="WebApplication"/>. Split from
/// <see cref="ServiceHost"/> for legibility; this file owns nothing but the
/// route map.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RouteRegistration
{
    public static void Register(
        WebApplication app,
        PlatformConfig config,
        X509Certificate2 cert,
        RequestLog requestLog,
        TokenStore tokenStore,
        IReadOnlyList<LoadedModule> loaded,
        AuthValidatorFactory validatorFactory,
        string platformVersion,
        bool port80Available)
    {
        var moduleRegistry = app.Services.GetRequiredService<ModuleRegistry>();

        // --- Per-module routing: auth → scope filter → MapMcp ---
        foreach (var m in loaded)
        {
            var moduleAuth = ResolveModuleAuth(config, m);
            var validator = validatorFactory.Build(moduleAuth);
            var modulePrefix = m.Manifest.MountPath; // e.g., "/math"
            var mcpPath = $"{modulePrefix}/mcp";

            app.UseWhen(
                ctx => ctx.Request.Path.StartsWithSegments(mcpPath),
                branch =>
                {
                    branch.UseMiddleware<RequestLogMiddleware>(requestLog, m.Name);
                    branch.UseMiddleware<AuthMiddleware>(validator);
                    branch.UseMiddleware<ModuleScopedMcpFilter>(moduleRegistry, m.Name);
                });

            app.MapMcp(mcpPath);
        }

        // --- Admin middleware on platform paths ---
        var adminValidator = validatorFactory.Build(config.Admin.Auth);
        app.UseWhen(
            ctx => IsAdminPath(ctx.Request.Path),
            branch => branch.UseMiddleware<AuthMiddleware>(adminValidator));

        // --- Platform endpoints (minimal v1.0 surface; dashboard polish lives in P4d) ---
        MapInfo(app, config, cert, loaded, platformVersion, port80Available);
        MapHealth(app);
        MapRequests(app, requestLog);
        MapCert(app, cert);

        if (NeedsTokenEndpoint(config))
        {
            MapToken(app, config, tokenStore);
        }
    }

    private static AuthDomainConfig ResolveModuleAuth(PlatformConfig config, LoadedModule m)
    {
        // Override priority: config.modules[name].authOverride > manifest.auth > config.mcp.defaultAuth.
        if (config.Modules.TryGetValue(m.Name, out var settings) && settings.AuthOverride is not null)
        {
            return MergeWithDemoCreds(settings.AuthOverride, config.Mcp.DefaultAuth);
        }
        if (m.Manifest.Auth is not null)
        {
            return MergeWithDemoCreds(m.Manifest.Auth, config.Mcp.DefaultAuth);
        }
        return config.Mcp.DefaultAuth;
    }

    private static AuthDomainConfig MergeWithDemoCreds(AuthDomainConfig requested, AuthDomainConfig platformDefault)
    {
        // Demo creds live only on the platform default. If a per-module override
        // says Mode=Demo, it must reference the platform's creds (we don't let
        // modules carry their own demo bundle in the manifest).
        if (requested.Mode == AuthMode.Demo && requested.DemoCredentials is null)
        {
            return new AuthDomainConfig
            {
                Mode = AuthMode.Demo,
                ProviderRef = requested.ProviderRef,
                RequiredScopes = requested.RequiredScopes,
                RequiredClaims = requested.RequiredClaims,
                DemoCredentials = platformDefault.DemoCredentials,
            };
        }
        return requested;
    }

    private static bool IsAdminPath(PathString path)
    {
        // Anything not under /<module>/mcp counts as admin. Module-scoped paths
        // are handled by per-module middleware above (UseWhen with explicit
        // match), so this catch-all auth applies to /, /info, /health, /logs,
        // /requests, /cert*, /token, /upgrade, /.well-known/*, etc.
        var p = path.Value ?? "/";
        // Cheap fast-path: known module mountPaths end with /mcp; admin paths
        // don't. This keeps the per-module branches above the only gate for
        // /<module>/mcp traffic.
        return !p.Contains("/mcp", StringComparison.Ordinal);
    }

    private static void MapInfo(
        WebApplication app, PlatformConfig config, X509Certificate2 cert,
        IReadOnlyList<LoadedModule> loaded, string platformVersion, bool port80Available)
    {
        app.MapGet("/", () => Results.Text(
            $"WinMCP {platformVersion} — dashboard arriving in v1.0 P4d. " +
            $"See /info for JSON metadata, /health for uptime.",
            "text/plain; charset=utf-8"));

        app.MapGet("/info", () => Results.Json(new
        {
            service = "WinMCP",
            version = platformVersion,
            status = "running",
            startedAt = ServiceHost.StartedAt.ToString("O"),
            uptimeSeconds = (long)(DateTime.UtcNow - ServiceHost.StartedAt).TotalSeconds,
            ports = new
            {
                http = config.HttpPort,
                https = config.HttpsPort,
                port80Active = port80Available,
            },
            host = new { machine = Environment.MachineName },
            modules = loaded.Select(m => new
            {
                name = m.Manifest.Name,
                version = m.Manifest.Version,
                displayName = m.Manifest.DisplayName,
                description = m.Manifest.Description,
                mountPath = m.Manifest.MountPath,
                maturity = m.Manifest.Maturity.ToString().ToLowerInvariant(),
                mcpEndpoint = $"{m.Manifest.MountPath}/mcp",
            }).ToArray(),
            admin = new { authMode = config.Admin.Auth.Mode.ToString().ToLowerInvariant() },
            mcp = new
            {
                defaultAuthMode = config.Mcp.DefaultAuth.Mode.ToString().ToLowerInvariant(),
                demoCredentials = (config.Mcp.DefaultAuth.Mode == AuthMode.Demo)
                    ? (object)new
                    {
                        bearerToken = config.Mcp.DefaultAuth.DemoCredentials?.BearerToken,
                        clientId = config.Mcp.DefaultAuth.DemoCredentials?.ClientId,
                        clientSecret = config.Mcp.DefaultAuth.DemoCredentials?.ClientSecret,
                        tokenTtlSeconds = config.Mcp.DefaultAuth.DemoCredentials?.TokenTtlSeconds,
                    }
                    : new { },
            },
            cert = new
            {
                notBefore = cert.NotBefore.ToString("O"),
                notAfter = cert.NotAfter.ToString("O"),
                fingerprintSha256 = ComputeFingerprint(cert),
            }
        }));
    }

    private static void MapHealth(WebApplication app) =>
        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            uptimeSeconds = (long)(DateTime.UtcNow - ServiceHost.StartedAt).TotalSeconds,
        }));

    private static void MapRequests(WebApplication app, RequestLog requestLog) =>
        app.MapGet("/requests", () => Results.Json(requestLog.Snapshot(),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }));

    private static void MapCert(WebApplication app, X509Certificate2 cert)
    {
        var derBytes = cert.Export(X509ContentType.Cert);
        var pemBytes = System.Text.Encoding.ASCII.GetBytes(
            "-----BEGIN CERTIFICATE-----\n" +
            Convert.ToBase64String(derBytes, Base64FormattingOptions.InsertLineBreaks) +
            "\n-----END CERTIFICATE-----\n");

        app.MapGet("/cert.cer", () => Results.File(derBytes, "application/pkix-cert", "winmcp.cer"));
        app.MapGet("/cert.pem", () => Results.File(pemBytes, "application/x-pem-file", "winmcp.pem"));
    }

    private static bool NeedsTokenEndpoint(PlatformConfig config) =>
        config.Mcp.DefaultAuth.Mode == AuthMode.Demo ||
        config.Modules.Values.Any(s => s.AuthOverride?.Mode == AuthMode.Demo);

    private static void MapToken(WebApplication app, PlatformConfig config, TokenStore tokenStore)
    {
        var creds = config.Mcp.DefaultAuth.DemoCredentials;
        if (creds is null)
        {
            // Misconfigured. /token endpoint not safe to mount; log + return.
            return;
        }

        app.MapPost("/token", async (HttpContext ctx) =>
        {
            if (!ctx.Request.HasFormContentType)
            {
                return Results.Json(new { error = "invalid_request", error_description = "expected application/x-www-form-urlencoded" }, statusCode: 400);
            }
            var form = await ctx.Request.ReadFormAsync();
            if (form["grant_type"].ToString() != "client_credentials")
            {
                return Results.Json(new { error = "unsupported_grant_type" }, statusCode: 400);
            }

            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.ClientSecret))
            {
                return Results.Json(new { error = "server_error", error_description = "client credentials not configured" }, statusCode: 503);
            }

            var presentedId = form["client_id"].ToString();
            var presentedSecret = form["client_secret"].ToString();
            var idOk = CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(presentedId),
                System.Text.Encoding.UTF8.GetBytes(creds.ClientId));
            var secretOk = CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(presentedSecret),
                System.Text.Encoding.UTF8.GetBytes(creds.ClientSecret));
            if (!idOk || !secretOk)
            {
                return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            }

            var token = tokenStore.Issue(TimeSpan.FromSeconds(creds.TokenTtlSeconds));
            return Results.Json(new
            {
                access_token = token,
                token_type = "Bearer",
                expires_in = creds.TokenTtlSeconds,
            });
        });
    }

    private static string ComputeFingerprint(X509Certificate2 cert)
    {
        var hash = SHA256.HashData(cert.RawData);
        var hex = Convert.ToHexString(hash);
        var sb = new System.Text.StringBuilder(hex.Length + hex.Length / 2);
        for (var i = 0; i < hex.Length; i += 2)
        {
            if (i > 0) sb.Append(':');
            sb.Append(hex, i, 2);
        }
        return sb.ToString();
    }
}
