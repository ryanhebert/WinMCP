using System.Text.RegularExpressions;
using WinMcp.ModuleSdk;

namespace WinMcp.Platform.Config;

/// <summary>
/// Pure shape + cross-reference validation for incoming settings payloads.
/// Mirrors the strictness of <see cref="Modules.ModuleManifestParser"/>:
/// reject on any malformed input; never silently save.
/// </summary>
/// <remarks>
/// All public methods are pure functions returning <see cref="ValidationResult"/>.
/// They never write, log, or otherwise mutate state — that's the API handler's job.
/// </remarks>
public static class SettingsValidator
{
    private static readonly Regex ProviderNameRegex = new(@"^[a-z][a-z0-9-]{0,62}$", RegexOptions.Compiled);

    public static ValidationResult ValidateProviderShape(string name, string issuer, string audience)
    {
        if (!ProviderNameRegex.IsMatch(name))
        {
            return ValidationResult.Fail("name", "must match ^[a-z][a-z0-9-]{0,62}$");
        }
        var issuerResult = ValidateIssuerUrl(issuer);
        if (!issuerResult.Ok) return issuerResult;
        if (string.IsNullOrWhiteSpace(audience) || audience.Any(char.IsWhiteSpace))
        {
            return ValidationResult.Fail("audience", "must be a single non-empty token");
        }
        return ValidationResult.Pass();
    }

    private static ValidationResult ValidateIssuerUrl(string issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return ValidationResult.Fail("issuer", "must not be empty");
        }
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri))
        {
            return ValidationResult.Fail("issuer", "must be an absolute URL");
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return ValidationResult.Fail("issuer", "must use http or https");
        }
        if (!string.IsNullOrEmpty(uri.Query))
        {
            return ValidationResult.Fail("issuer", "must not include a query string");
        }
        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            return ValidationResult.Fail("issuer", "must not include a URL fragment");
        }
        // Reject path traversal segments in the *original* string — Uri.AbsolutePath
        // normalises "/../" away before we can inspect it, so we check the raw input.
        var rawPath = issuer.Substring(issuer.IndexOf("://", StringComparison.Ordinal) + 3);
        if (rawPath.Split('/').Any(s => s == ".." || s == "."))
        {
            return ValidationResult.Fail("issuer", "must not contain path traversal");
        }
        return ValidationResult.Pass();
    }

    private static readonly HashSet<string> ValidModes = new(StringComparer.Ordinal) { "none", "demo", "oidc" };

    public static ValidationResult ValidateAuthDomainShape(
        string mode,
        string? providerRef,
        IReadOnlyList<string> requiredScopes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> requiredClaims,
        IReadOnlySet<string> knownProviders)
    {
        if (!ValidModes.Contains(mode))
        {
            return ValidationResult.Fail("mode", $"must be one of 'none', 'demo', 'oidc' (got '{mode}')");
        }

        if (mode == "oidc")
        {
            if (string.IsNullOrWhiteSpace(providerRef))
            {
                return ValidationResult.Fail("providerRef", "must reference a configured identity provider");
            }
            if (!knownProviders.Contains(providerRef))
            {
                return ValidationResult.Fail("providerRef", $"provider '{providerRef}' is not configured");
            }
        }

        foreach (var scope in requiredScopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                return ValidationResult.Fail("requiredScopes", "scope entries must not be empty");
            }
        }

        foreach (var (key, values) in requiredClaims)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return ValidationResult.Fail("requiredClaims", "claim names must not be empty");
            }
            if (values.Count == 0)
            {
                return ValidationResult.Fail("requiredClaims", $"claim '{key}' must have at least one value");
            }
            foreach (var v in values)
            {
                if (string.IsNullOrWhiteSpace(v))
                {
                    return ValidationResult.Fail("requiredClaims", $"claim '{key}' has an empty value");
                }
            }
        }

        return ValidationResult.Pass();
    }

    /// <summary>
    /// Rejects deletion of an OIDC provider that the admin or MCP auth
    /// domain — or any per-module auth override — still references.
    /// Returns the referencing domains in the error message so the
    /// operator knows which setting to change first.
    /// </summary>
    public static ValidationResult ValidateProviderDeletable(string name, PlatformConfig config)
    {
        var inUseBy = new List<string>();
        if (config.Admin.Auth.Mode == AuthMode.Oidc &&
            string.Equals(config.Admin.Auth.ProviderRef, name, StringComparison.Ordinal))
        {
            inUseBy.Add("admin");
        }
        if (config.Mcp.DefaultAuth.Mode == AuthMode.Oidc &&
            string.Equals(config.Mcp.DefaultAuth.ProviderRef, name, StringComparison.Ordinal))
        {
            inUseBy.Add("mcp");
        }
        foreach (var (moduleName, settings) in config.Modules)
        {
            if (settings.AuthOverride is { Mode: AuthMode.Oidc } overrideAuth &&
                string.Equals(overrideAuth.ProviderRef, name, StringComparison.Ordinal))
            {
                inUseBy.Add($"module:{moduleName}");
            }
        }
        if (inUseBy.Count > 0)
        {
            return ValidationResult.Fail(
                "name",
                $"provider '{name}' is in use by: {string.Join(", ", inUseBy)}. Change those settings before deleting.",
                inUseBy);
        }
        return ValidationResult.Pass();
    }
}

public sealed record ValidationResult(bool Ok, string? Field, string? Message, IReadOnlyList<string>? InUseBy = null)
{
    public static ValidationResult Pass() => new(true, null, null);
    public static ValidationResult Fail(string field, string message) => new(false, field, message);
    public static ValidationResult Fail(string field, string message, IReadOnlyList<string> inUseBy) =>
        new(false, field, message, inUseBy);
}
