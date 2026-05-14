using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WinMcp.Platform.Auth.Oidc;

/// <summary>
/// Hydrates an <see cref="OidcProvider"/> from its issuer URL by fetching
/// the standard <c>/.well-known/openid-configuration</c> metadata document
/// (RFC 8414 / OIDC Discovery 1.0). Populates <c>JwksUrl</c>, endpoints, and
/// validates that <c>jwks_uri</c> is reachable.
/// </summary>
/// <remarks>
/// v1.0 only does the discovery + reachability check — actual JWT
/// validation against the JWKS lands in v1.1 when <see cref="OidcValidator"/>
/// stops returning 503.
/// </remarks>
public sealed class OidcDiscovery
{
    private readonly HttpClient _http;
    private readonly ILogger<OidcDiscovery> _logger;

    public OidcDiscovery(HttpClient http, ILogger<OidcDiscovery> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Fetches the issuer's discovery document and populates the supplied
    /// provider. Throws on transport or parse failure — callers are expected
    /// to catch at startup and log a clear error.
    /// </summary>
    public async Task DiscoverAsync(OidcProvider provider, CancellationToken ct = default)
    {
        var url = TrimTrailingSlash(provider.Issuer) + "/.well-known/openid-configuration";
        _logger.LogInformation(
            "OIDC discovery: provider={Provider} fetching {Url}",
            provider.Name, url);

        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var root = doc.RootElement;
        provider.JwksUrl = root.TryGetProperty("jwks_uri", out var j) ? j.GetString() : null;
        provider.AuthorizationEndpoint = root.TryGetProperty("authorization_endpoint", out var a) ? a.GetString() : null;
        provider.TokenEndpoint = root.TryGetProperty("token_endpoint", out var t) ? t.GetString() : null;
        provider.DiscoveredAtUtc = DateTime.UtcNow;

        if (string.IsNullOrEmpty(provider.JwksUrl))
        {
            throw new InvalidOperationException(
                $"OIDC discovery for provider '{provider.Name}' (issuer={provider.Issuer}) returned no jwks_uri.");
        }

        // Reachability probe — fail loud at startup if the JWKS endpoint
        // is unreachable. Avoids the v1.1 OIDC validator hitting a 404 on
        // first protected request.
        using var jwksResponse = await _http.GetAsync(provider.JwksUrl, ct).ConfigureAwait(false);
        if (!jwksResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OIDC discovery for provider '{provider.Name}': jwks_uri {provider.JwksUrl} returned {(int)jwksResponse.StatusCode}.");
        }

        _logger.LogInformation(
            "OIDC discovery: provider={Provider} ok. jwks_uri={Jwks}, token_endpoint={Token}",
            provider.Name, provider.JwksUrl, provider.TokenEndpoint ?? "(none)");
    }

    private static string TrimTrailingSlash(string s) =>
        s.EndsWith('/') ? s.TrimEnd('/') : s;
}
