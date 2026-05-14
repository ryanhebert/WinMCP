using System.Text.Json.Serialization;
using WinMcp.Platform.Auth;

namespace WinMcp.Platform.Modules;

/// <summary>
/// Typed model of a module's <c>module.json</c>. Parsed via
/// <see cref="ModuleManifestParser"/> with strict validation.
/// </summary>
/// <remarks>
/// Schema reference:
/// https://github.com/ryanhebert/WinMCP-Modules/blob/main/docs/MANIFEST.md
/// </remarks>
public sealed class ModuleManifest
{
    // === Required ===
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("assembly")]
    public required string Assembly { get; init; }

    [JsonPropertyName("entryType")]
    public required string EntryType { get; init; }

    [JsonPropertyName("mountPath")]
    public required string MountPath { get; init; }

    [JsonPropertyName("minPlatformVersion")]
    public required string MinPlatformVersion { get; init; }

    // === Recommended ===
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("maturity")]
    public ModuleMaturity Maturity { get; init; } = ModuleMaturity.Experimental;

    [JsonPropertyName("publisher")]
    public ModulePublisher? Publisher { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    // === Reserved (v1.0 accepts; v1.1+ enforces) ===
    [JsonPropertyName("auth")]
    public AuthDomainConfig? Auth { get; init; }

    [JsonPropertyName("requiredEntitlements")]
    public IReadOnlyList<string> RequiredEntitlements { get; init; } = Array.Empty<string>();

    [JsonPropertyName("minMcpProtocolVersion")]
    public string? MinMcpProtocolVersion { get; init; }

    [JsonPropertyName("maxMcpProtocolVersion")]
    public string? MaxMcpProtocolVersion { get; init; }

    [JsonPropertyName("signature")]
    public object? Signature { get; init; }

    // === Optional auto-upgrade source ===
    [JsonPropertyName("updateSource")]
    public ModuleUpdateSource? UpdateSource { get; init; }
}

/// <summary>
/// Describes where a module's release artefacts live so the platform can
/// fetch a newer version on operator request. Modules without an
/// updateSource block don't get an in-UI Upgrade button — operators upgrade
/// them by manually replacing the folder and restarting the service.
/// </summary>
public sealed class ModuleUpdateSource
{
    /// <summary>v1.0 supports <c>github-releases</c> only.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>e.g. <c>ryanhebert/WinMCP-Modules</c>.</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>
    /// Release-asset filename template. Must contain <c>{version}</c>, which
    /// is replaced with the release tag at resolution time (e.g.
    /// <c>math-{version}.zip</c> → <c>math-v1.2.0.zip</c>).
    /// </summary>
    [JsonPropertyName("asset")]
    public required string Asset { get; init; }
}

public sealed class ModulePublisher
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("verified")]
    public bool Verified { get; init; }
}

public enum ModuleMaturity
{
    // JSON values are lowercase ("demo"/"experimental"/"production"); mapping
    // is handled by JsonStringEnumConverter with camelCase naming policy in
    // ModuleManifestParser. .NET 8 doesn't ship JsonStringEnumMemberName.
    Demo,
    Experimental,
    Production
}
