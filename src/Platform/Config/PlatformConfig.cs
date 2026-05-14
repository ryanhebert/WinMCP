using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WinMcp.Platform.Auth;

namespace WinMcp.Platform.Config;

/// <summary>
/// Root configuration object loaded from <c>&lt;InstallDir&gt;\config.json</c>.
/// Operator-managed; the platform itself rewrites only when the operator
/// edits settings via the dashboard (v1.1+) or runs CLI commands that mutate
/// state (e.g., <c>rotate-creds</c>).
/// </summary>
public sealed class PlatformConfig
{
    [JsonPropertyName("httpPort")]
    public int HttpPort { get; set; } = 52080;

    [JsonPropertyName("httpsPort")]
    public int HttpsPort { get; set; } = 52443;

    [JsonPropertyName("logLevel")]
    public string LogLevel { get; set; } = "Information";

    [JsonPropertyName("admin")]
    public AdminSection Admin { get; set; } = new();

    [JsonPropertyName("mcp")]
    public McpSection Mcp { get; set; } = new();

    [JsonPropertyName("oidcProviders")]
    public Dictionary<string, OidcProviderConfig> OidcProviders { get; set; } = new();

    [JsonPropertyName("modules")]
    public Dictionary<string, ModuleSettings> Modules { get; set; } = new();

    [JsonPropertyName("upgrade")]
    public UpgradeSection Upgrade { get; set; } = new();
}

public sealed class AdminSection
{
    [JsonPropertyName("auth")]
    public AuthDomainConfig Auth { get; set; } = new();
}

public sealed class McpSection
{
    [JsonPropertyName("defaultAuth")]
    public AuthDomainConfig DefaultAuth { get; set; } = new();

    [JsonPropertyName("allowDemoModulesInProduction")]
    public bool AllowDemoModulesInProduction { get; set; } = false;
}

/// <summary>Operator-provided OIDC provider config; <see cref="JwksUrl"/> and endpoints are auto-discovered.</summary>
public sealed class OidcProviderConfig
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("audience")]
    public string? Audience { get; init; }

    [JsonPropertyName("jwksUrl")]
    public string? JwksUrl { get; set; }

    [JsonPropertyName("authorizationEndpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("tokenEndpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("discoveredAtIso")]
    public string? DiscoveredAtIso { get; set; }
}

/// <summary>Per-module operator settings — separate from the module's own <c>module.json</c>.</summary>
public sealed class ModuleSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>If set, overrides the module manifest's <c>auth</c> field for this install.</summary>
    [JsonPropertyName("authOverride")]
    public AuthDomainConfig? AuthOverride { get; set; }

    /// <summary>Free-form per-module config slice (API keys, etc.) exposed to the module at Configure time.</summary>
    [JsonPropertyName("moduleConfig")]
    public JsonObject? ModuleConfig { get; set; }
}

public sealed class UpgradeSection
{
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";
}
