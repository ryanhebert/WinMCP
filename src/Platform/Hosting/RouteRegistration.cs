using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using WinMcp.ModuleSdk;
using WinMcp.Platform.Auth;
using WinMcp.Platform.Config;
using WinMcp.Platform.Modules;
using WinMcp.Platform.Web;

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
        var resolvedAuth = loaded.ToDictionary(m => m.Name, m => ResolveModuleAuth(config, m));

        // --- Port-80 gating: when the platform binds :80, only /token (the
        // anonymous-HTTP OAuth2 path) responds there. Everything else 404s
        // back to the configured HTTP/HTTPS ports. Carried forward from
        // math-mcp v1.0.21.
        if (port80Available)
        {
            app.Use(async (context, next) =>
            {
                if (context.Connection.LocalPort == 80 &&
                    !context.Request.Path.StartsWithSegments("/token"))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync(
                        "Port 80 on this server serves only the OAuth /token endpoint. " +
                        "Everything else (dashboard, /<module>/mcp, /logs, /.well-known/*, etc.) " +
                        $"is available on http://<host>:{config.HttpPort}/ (HTTP) and " +
                        $"https://<host>:{config.HttpsPort}/ (HTTPS).");
                    return;
                }
                await next();
            });
        }

        // --- Per-module routing: auth → scope filter → MapMcp ---
        foreach (var m in loaded)
        {
            var validator = validatorFactory.Build(resolvedAuth[m.Name]);
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

        // --- Cache-Control: no-store on the dashboard's data endpoints so a
        // tab left open across an upgrade doesn't keep showing stale values. ---
        app.Use(async (ctx, next) =>
        {
            var p = ctx.Request.Path.Value ?? "";
            if (p == "/" ||
                p.StartsWith("/info", StringComparison.Ordinal) ||
                p.StartsWith("/health", StringComparison.Ordinal) ||
                p.StartsWith("/requests", StringComparison.Ordinal) ||
                p.StartsWith("/logs", StringComparison.Ordinal) ||
                p.StartsWith("/upgrade", StringComparison.Ordinal))
            {
                ctx.Response.Headers.CacheControl = "no-store";
            }
            await next();
        });

        // --- Platform endpoints ---
        var anyDemoAuth = config.Mcp.DefaultAuth.Mode == AuthMode.Demo
            || resolvedAuth.Values.Any(a => a.Mode == AuthMode.Demo);

        MapIndex(app, config, cert, loaded, moduleRegistry, resolvedAuth, platformVersion, port80Available);
        MapInfo(app, config, cert, loaded, platformVersion, port80Available);
        MapHealth(app);
        MapRequests(app, requestLog);
        MapCert(app, cert);
        MapLogs(app);
        MapLogsTail(app);
        MapLogsDates(app);
        MapFavicon(app);
        UpgradeOrchestrator.MapEndpoints(app);

        if (anyDemoAuth)
        {
            MapWellKnown(app, config, loaded, resolvedAuth);
        }

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
        return !p.Contains("/mcp", StringComparison.Ordinal);
    }

    private static void MapIndex(
        WebApplication app, PlatformConfig config, X509Certificate2 cert,
        IReadOnlyList<LoadedModule> loaded, ModuleRegistry registry,
        IReadOnlyDictionary<string, AuthDomainConfig> resolvedAuth,
        string platformVersion, bool port80Available)
    {
        app.MapGet("/", (RequestLog rl) =>
        {
            var modules = loaded
                .Select(m =>
                {
                    var scope = registry.Scopes.TryGetValue(m.Name, out var s) ? s : null;
                    var auth = resolvedAuth.TryGetValue(m.Name, out var a) ? a.Mode : config.Mcp.DefaultAuth.Mode;
                    return new ModuleDisplay(
                        Name: m.Manifest.Name,
                        Version: m.Manifest.Version,
                        DisplayName: m.Manifest.DisplayName ?? m.Manifest.Name,
                        Description: m.Manifest.Description ?? string.Empty,
                        MountPath: m.Manifest.MountPath,
                        Maturity: m.Manifest.Maturity.ToString().ToLowerInvariant(),
                        AuthMode: auth.ToString().ToLowerInvariant(),
                        HasUpdateSource: m.Manifest.UpdateSource is not null,
                        ToolNames: scope?.Tools.OrderBy(t => t, StringComparer.Ordinal).ToList() ?? new List<string>(),
                        PromptNames: scope?.Prompts.OrderBy(p => p, StringComparer.Ordinal).ToList() ?? new List<string>(),
                        ResourceNames: scope?.Resources.OrderBy(r => r, StringComparer.Ordinal).ToList() ?? new List<string>());
                })
                .ToList();

            PlatformDemoCredentials? demo = null;
            if (config.Mcp.DefaultAuth.Mode == AuthMode.Demo && config.Mcp.DefaultAuth.DemoCredentials is { } creds)
            {
                demo = new PlatformDemoCredentials(
                    BearerToken: creds.BearerToken ?? "",
                    ClientId: creds.ClientId ?? "",
                    ClientSecret: creds.ClientSecret ?? "",
                    TokenTtlSeconds: creds.TokenTtlSeconds);
            }

            var model = new IndexPageModel(
                Version: platformVersion,
                MachineName: Environment.MachineName,
                Os: Environment.OSVersion.ToString(),
                HttpPort: config.HttpPort,
                HttpsPort: config.HttpsPort,
                Port80Active: port80Available,
                StartedAtIso: ServiceHost.StartedAt.ToString("O"),
                AdminAuthMode: config.Admin.Auth.Mode.ToString().ToLowerInvariant(),
                McpDefaultAuthMode: config.Mcp.DefaultAuth.Mode.ToString().ToLowerInvariant(),
                PlatformDemo: demo,
                CertFingerprint: ComputeFingerprint(cert),
                CertNotBefore: cert.NotBefore.ToString("yyyy-MM-dd"),
                CertNotAfter: cert.NotAfter.ToString("yyyy-MM-dd"),
                Modules: modules,
                RecentRequests: rl.Snapshot());

            return Results.Content(IndexPage.Render(model), "text/html; charset=utf-8");
        });
    }

    private static void MapInfo(
        WebApplication app, PlatformConfig config, X509Certificate2 cert,
        IReadOnlyList<LoadedModule> loaded, string platformVersion, bool port80Available)
    {
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

    private static void MapLogs(WebApplication app)
    {
        app.MapGet("/logs", () =>
        {
            var fileName = $"winmcp-{DateTime.Now:yyyyMMdd}.log";
            var filePath = Path.Combine(PlatformPaths.LogDir, fileName);
            var html = LogsPage.Render(new LogsPageModel(
                LogFileName: fileName,
                LogFilePath: filePath));
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }

    private static void MapLogsTail(WebApplication app)
    {
        app.MapGet("/logs/tail", (int? n, string? date) =>
        {
            var count = Math.Clamp(n ?? 500, 1, 5000);
            // Validate the optional date: yyyy-MM-dd only. Anything else
            // falls back to today and is path-traversal-safe.
            string dateStamp;
            if (!string.IsNullOrEmpty(date) && Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            {
                dateStamp = date.Replace("-", "");
            }
            else
            {
                dateStamp = DateTime.Now.ToString("yyyyMMdd");
            }

            var filePath = Path.Combine(PlatformPaths.LogDir, $"winmcp-{dateStamp}.log");
            if (!File.Exists(filePath)) return Results.Text("", "text/plain; charset=utf-8");

            var text = LogReader.ReadLastLines(filePath, count);
            return Results.Text(text, "text/plain; charset=utf-8");
        });
    }

    private static void MapLogsDates(WebApplication app)
    {
        // List which dated log files exist (newest first). Lets the UI
        // populate a date picker; daily retention is 30 days per the
        // Serilog config.
        app.MapGet("/logs/dates", () =>
        {
            if (!Directory.Exists(PlatformPaths.LogDir))
            {
                return Results.Json(Array.Empty<string>());
            }
            var re = new Regex(@"^winmcp-(\d{4})(\d{2})(\d{2})\.log$");
            var dates = Directory.EnumerateFiles(PlatformPaths.LogDir, "winmcp-*.log")
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Select(n => re.Match(n!))
                .Where(m => m.Success)
                .Select(m => $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}")
                .OrderByDescending(d => d)
                .ToArray();
            return Results.Json(dates);
        });
    }

    private static void MapFavicon(WebApplication app)
    {
        app.MapGet("/favicon.svg", () => Results.File(Favicon.Bytes, "image/svg+xml"));
        // Fallback: many browsers still request /favicon.ico — serve the SVG
        // bytes anyway. Modern browsers render it; older ones just see no
        // favicon. Better than logging a 404 per visit.
        app.MapGet("/favicon.ico", () => Results.File(Favicon.Bytes, "image/svg+xml"));
    }

    private static void MapWellKnown(
        WebApplication app, PlatformConfig config,
        IReadOnlyList<LoadedModule> loaded,
        IReadOnlyDictionary<string, AuthDomainConfig> resolvedAuth)
    {
        // OAuth 2.0 Authorization Server Metadata (RFC 8414) + OIDC alias.
        // /token is platform-wide in demo mode, so one metadata document
        // covers any module that delegates to it.
        IResult MetadataHandler(HttpContext ctx)
        {
            var origin = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}";
            return Results.Json(new
            {
                issuer = origin,
                token_endpoint = $"{origin}/token",
                grant_types_supported = new[] { "client_credentials" },
                token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
                response_types_supported = Array.Empty<string>(),
                scopes_supported = Array.Empty<string>(),
            });
        }
        app.MapGet("/.well-known/oauth-authorization-server", MetadataHandler);
        app.MapGet("/.well-known/openid-configuration", MetadataHandler);

        // RFC 9728 protected-resource metadata, scoped per resource: a request
        // for /.well-known/oauth-protected-resource/math/mcp returns the doc
        // for that specific module's MCP endpoint. Anything else 404s.
        var demoResources = loaded
            .Where(m => resolvedAuth.TryGetValue(m.Name, out var a) && a.Mode == AuthMode.Demo)
            .Select(m => $"{m.Manifest.MountPath}/mcp")
            .ToHashSet(StringComparer.Ordinal);

        app.MapGet("/.well-known/oauth-protected-resource/{**path}", (HttpContext ctx, string path) =>
        {
            var resourcePath = "/" + path;
            if (!demoResources.Contains(resourcePath))
            {
                return Results.NotFound();
            }
            var origin = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}";
            return Results.Json(new
            {
                resource = $"{origin}{resourcePath}",
                authorization_servers = new[] { origin },
                bearer_methods_supported = new[] { "header" },
                resource_documentation = $"{origin}/info",
            });
        });
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
