using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinMcp.Platform.Modules;

/// <summary>
/// Parses and validates <c>module.json</c> files. Strict on required fields
/// and field types; tolerant of unknown top-level keys (forward compat).
/// </summary>
public static class ModuleManifestParser
{
    private static readonly Regex NameRegex = new(@"^[a-z][a-z0-9-]{0,62}$", RegexOptions.Compiled);
    private static readonly Regex SemVerRegex = new(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.Compiled);
    private static readonly Regex GithubRepoRegex = new(
        @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled);
    private const string UpdateSourceTypeGithubReleases = "github-releases";

    private static readonly HashSet<string> ReservedMountPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/", "/info", "/health", "/logs", "/requests", "/token", "/upgrade", "/cert.cer", "/cert.pem"
    };

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parse and validate a <c>module.json</c> file. Throws on missing
    /// required fields, malformed values, or reserved mount-path collisions.
    /// </summary>
    public static ModuleManifest ParseFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new ModuleManifestException($"Failed to read module manifest at {path}: {ex.Message}", ex);
        }

        return Parse(json, sourceLabel: path);
    }

    public static ModuleManifest Parse(string json, string sourceLabel = "<inline>")
    {
        ModuleManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ModuleManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ModuleManifestException($"Invalid JSON in {sourceLabel}: {ex.Message}", ex);
        }

        if (manifest is null)
        {
            throw new ModuleManifestException($"Manifest in {sourceLabel} parsed to null.");
        }

        Validate(manifest, sourceLabel);
        return manifest;
    }

    private static void Validate(ModuleManifest m, string sourceLabel)
    {
        void Fail(string field, string why) =>
            throw new ModuleManifestException($"{sourceLabel}: {field} {why}");

        if (!NameRegex.IsMatch(m.Name))
        {
            Fail("name", $"'{m.Name}' must match {NameRegex}");
        }
        if (!SemVerRegex.IsMatch(m.Version))
        {
            Fail("version", $"'{m.Version}' is not valid SemVer");
        }
        if (string.IsNullOrWhiteSpace(m.DisplayName))
        {
            Fail("displayName", "must not be empty");
        }
        if (string.IsNullOrWhiteSpace(m.Assembly) || m.Assembly.Contains('/') || m.Assembly.Contains('\\'))
        {
            Fail("assembly", $"'{m.Assembly}' must be a bare filename (no path separators)");
        }
        if (string.IsNullOrWhiteSpace(m.EntryType))
        {
            Fail("entryType", "must not be empty");
        }
        if (!m.MountPath.StartsWith('/'))
        {
            Fail("mountPath", $"'{m.MountPath}' must start with /");
        }
        if (m.MountPath.Contains(".."))
        {
            Fail("mountPath", $"'{m.MountPath}' must not contain '..'");
        }
        if (ReservedMountPaths.Contains(m.MountPath))
        {
            Fail("mountPath", $"'{m.MountPath}' collides with a platform-reserved path");
        }
        if (!SemVerRegex.IsMatch(m.MinPlatformVersion))
        {
            Fail("minPlatformVersion", $"'{m.MinPlatformVersion}' is not valid SemVer");
        }
        if (m.MinMcpProtocolVersion is { Length: > 0 } && !LooksLikeMcpDate(m.MinMcpProtocolVersion))
        {
            Fail("minMcpProtocolVersion", $"'{m.MinMcpProtocolVersion}' is not a recognized MCP protocol date string");
        }

        // Mount path's URL segment must match the module name. This keeps
        // /<module>/mcp predictable and matches the platform's expectations.
        var expected = "/" + m.Name;
        if (!string.Equals(m.MountPath, expected, StringComparison.Ordinal))
        {
            Fail("mountPath", $"'{m.MountPath}' must be exactly '{expected}' to match the module name");
        }

        if (m.UpdateSource is { } u)
        {
            if (!string.Equals(u.Type, UpdateSourceTypeGithubReleases, StringComparison.Ordinal))
            {
                Fail("updateSource.type", $"'{u.Type}' is not supported (v1.0 accepts '{UpdateSourceTypeGithubReleases}' only)");
            }
            if (string.IsNullOrWhiteSpace(u.Repo) || !GithubRepoRegex.IsMatch(u.Repo))
            {
                Fail("updateSource.repo", $"'{u.Repo}' must be in 'owner/repo' form");
            }
            if (string.IsNullOrWhiteSpace(u.Asset) || !u.Asset.Contains("{version}", StringComparison.Ordinal))
            {
                Fail("updateSource.asset", $"'{u.Asset}' must contain the literal '{{version}}' placeholder");
            }
            if (u.Asset.Contains('/') || u.Asset.Contains('\\'))
            {
                Fail("updateSource.asset", $"'{u.Asset}' must be a bare filename (no path separators)");
            }
        }
    }

    private static bool LooksLikeMcpDate(string s) =>
        s.Length == 10 && s[4] == '-' && s[7] == '-' && DateOnly.TryParse(s, out _);
}

public sealed class ModuleManifestException : Exception
{
    public ModuleManifestException(string message) : base(message) { }
    public ModuleManifestException(string message, Exception inner) : base(message, inner) { }
}
