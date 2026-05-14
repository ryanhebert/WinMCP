using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using Serilog;
using Serilog.Events;
using WinMcp.ModuleSdk;
using WinMcp.Platform.Auth;
using WinMcp.Platform.Auth.Oidc;
using WinMcp.Platform.Config;
using WinMcp.Platform.Modules;
using WinMcp.Platform.Net;
using WinMcp.Platform.Tls;

namespace WinMcp.Platform.Hosting;

[SupportedOSPlatform("windows")]
public static class ServiceHost
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    public static int Run(bool asWindowsService)
    {
        if (!File.Exists(PlatformPaths.ConfigPath))
        {
            Console.Error.WriteLine($"Config not found: {PlatformPaths.ConfigPath}");
            Console.Error.WriteLine("Run WinMCP.exe (with no args) as administrator to install.");
            return 1;
        }
        if (!File.Exists(PlatformPaths.CertPath))
        {
            Console.Error.WriteLine($"Cert not found: {PlatformPaths.CertPath}");
            Console.Error.WriteLine("Run WinMCP.exe (with no args) as administrator to install.");
            return 1;
        }

        var config = ConfigLoader.Load(PlatformPaths.ConfigPath);

        // Auto-renew cert if within 30 days of expiry (v1.0.19 behavior carries forward).
        var prevExpiry = CertificateProvider.DescribeExpiry(PlatformPaths.CertPath);
        var certResult = CertificateProvider.EnsureCert(PlatformPaths.CertPath);
        var cert = CertificateProvider.Load(PlatformPaths.CertPath);

        Directory.CreateDirectory(PlatformPaths.LogDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(ParseSerilogLevel(config.LogLevel))
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(PlatformPaths.LogDir, "winmcp-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                shared: true)
            .CreateLogger();

        try
        {
            return RunCore(asWindowsService, config, cert, certResult, prevExpiry);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Service terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static int RunCore(
        bool asWindowsService,
        PlatformConfig config,
        X509Certificate2 cert,
        CertificateProvider.EnsureResult certResult,
        string prevExpiry)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Host.UseSerilog();

        if (asWindowsService)
        {
            builder.Services.AddWindowsService(o => o.ServiceName = PlatformPaths.ServiceName);
        }

        // Shared infrastructure singletons.
        var requestLog = new RequestLog();
        builder.Services.AddSingleton(requestLog);

        var tokenStore = new TokenStore();
        builder.Services.AddSingleton(tokenStore);

        builder.Services.AddHttpClient();

        // CORS — test-server posture. Per-module auth gates real access.
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
            .AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
            .WithExposedHeaders("WWW-Authenticate", "Mcp-Session-Id")));

        // OIDC providers — config-only in v1.0. Discovery happens after Build().
        var oidcProviders = config.OidcProviders
            .Select(kvp => new OidcProvider
            {
                Name = kvp.Key,
                Issuer = kvp.Value.Issuer,
                Audience = kvp.Value.Audience ?? "winmcp",
                JwksUrl = kvp.Value.JwksUrl,
                AuthorizationEndpoint = kvp.Value.AuthorizationEndpoint,
                TokenEndpoint = kvp.Value.TokenEndpoint,
            })
            .ToList();
        var oidcRegistry = new OidcProviderRegistry(oidcProviders);
        builder.Services.AddSingleton(oidcRegistry);
        builder.Services.AddTransient<OidcDiscovery>();

        // Single MCP server in DI — the SDK only supports one. Per-module
        // routing + scope filtering recovers the multi-module URL semantics.
        var mcpBuilder = builder.Services.AddMcpServer().WithHttpTransport();

        // Load modules and wire them into the shared MCP server.
        var platformVersion = ResolvePlatformVersion();
        var preLoadLogger = LoggerFactory.Create(b => b.AddSerilog()).CreateLogger<ModuleLoader>();
        var loader = new ModuleLoader(preLoadLogger, platformVersion);
        var loaded = loader.ScanAndLoad(PlatformPaths.ModulesDir);

        var moduleDiscoveries = new List<(string Module, ModuleToolDiscovery.DiscoveryResult Disco)>();
        var platformInfo = new PlatformInfo
        {
            PlatformVersion = platformVersion,
            McpProtocolVersion = "2025-06-18",
            AdminAuthMode = config.Admin.Auth.Mode,
            McpDefaultAuthMode = config.Mcp.DefaultAuth.Mode,
        };

        foreach (var m in loaded)
        {
            var asm = m.Instance.GetType().Assembly;
            mcpBuilder.WithToolsFromAssembly(asm);
            mcpBuilder.WithPromptsFromAssembly(asm);
            mcpBuilder.WithResourcesFromAssembly(asm);

            var disco = ModuleToolDiscovery.Discover(asm);
            moduleDiscoveries.Add((m.Name, disco));

            var moduleLogger = LoggerFactory.Create(b => b.AddSerilog())
                .CreateLogger($"WinMcpModule.{m.Name}");
            var moduleConfig = ResolveModuleConfig(config, m.Name);
            var ctx = new ModuleConfigurationContext
            {
                Mcp = mcpBuilder,
                Services = builder.Services,
                ModuleConfig = moduleConfig,
                Logger = moduleLogger,
                Platform = platformInfo,
            };
            m.Instance.Configure(ctx);
        }

        var moduleRegistry = new ModuleRegistry(moduleDiscoveries);
        builder.Services.AddSingleton(moduleRegistry);
        builder.Services.AddSingleton<AuthValidatorFactory>();

        // Kestrel binding.
        var port80Available = NetInfo.TryProbeFreePort(80);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Any, config.HttpPort);
            options.Listen(System.Net.IPAddress.Any, config.HttpsPort, listen => listen.UseHttps(cert));
            if (port80Available) options.Listen(System.Net.IPAddress.Any, 80);
        });

        var app = builder.Build();

        var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WinMcp");
        startupLogger.LogInformation(
            "WinMCP {Version} starting. HTTP={Http} HTTPS={Https} port80={Port80} adminAuth={Admin} mcpAuth={Mcp}",
            platformVersion, config.HttpPort, config.HttpsPort,
            port80Available ? "active" : "skipped",
            config.Admin.Auth.Mode, config.Mcp.DefaultAuth.Mode);

        UpgradeOrchestrator.CleanupStaleArtefacts(startupLogger);

        if (certResult == CertificateProvider.EnsureResult.Renewed)
        {
            startupLogger.LogWarning(
                "TLS cert auto-renewed. previous_not_after={Prev} new_not_after={New}",
                prevExpiry, cert.NotAfter.ToString("yyyy-MM-dd"));
        }

        startupLogger.LogInformation(
            "Modules loaded: {Count}. {Names}",
            loaded.Count,
            string.Join(", ", loaded.Select(m => $"{m.Name}@{m.Manifest.Version} ({m.Manifest.MountPath})")));

        // Best-effort OIDC discovery for any configured providers. Failures
        // are logged but non-fatal — operators can fix config and restart.
        RunOidcDiscovery(app, oidcRegistry, config).GetAwaiter().GetResult();

        var validatorFactory = app.Services.GetRequiredService<AuthValidatorFactory>();

        app.UseCors();
        RouteRegistration.Register(app, config, cert, requestLog, tokenStore, loaded, validatorFactory, platformVersion, port80Available);

        app.Run();
        return 0;
    }

    private static IConfiguration ResolveModuleConfig(PlatformConfig config, string moduleName)
    {
        if (!config.Modules.TryGetValue(moduleName, out var settings) ||
            settings.ModuleConfig is null)
        {
            return new ConfigurationBuilder().Build();
        }

        var json = settings.ModuleConfig.ToJsonString();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    private static async Task RunOidcDiscovery(
        WebApplication app, OidcProviderRegistry registry, PlatformConfig config)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WinMcp.Oidc");
        var http = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        var disco = new OidcDiscovery(http, app.Services.GetRequiredService<ILogger<OidcDiscovery>>());

        foreach (var provider in registry.All)
        {
            try
            {
                await disco.DiscoverAsync(provider);
                if (config.OidcProviders.TryGetValue(provider.Name, out var raw))
                {
                    raw.JwksUrl = provider.JwksUrl;
                    raw.AuthorizationEndpoint = provider.AuthorizationEndpoint;
                    raw.TokenEndpoint = provider.TokenEndpoint;
                    raw.DiscoveredAtIso = DateTime.UtcNow.ToString("O");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "OIDC discovery failed for provider={Provider} issuer={Issuer}. " +
                    "Validators referencing this provider will return 503 until next restart.",
                    provider.Name, provider.Issuer);
            }
        }
    }

    private static string ResolvePlatformVersion() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    private static LogEventLevel ParseSerilogLevel(string s) => s.ToLowerInvariant() switch
    {
        "trace" => LogEventLevel.Verbose,
        "debug" => LogEventLevel.Debug,
        "information" or "info" => LogEventLevel.Information,
        "warning" or "warn" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "critical" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };

    public static DateTime StartedAt => StartedAtUtc;
}
