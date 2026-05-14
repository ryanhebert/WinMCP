using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WinMcp.Platform.Auth.Oidc;

namespace WinMcp.Platform.Auth;

/// <summary>
/// Stubbed OIDC validator for v1.0. Accepts config (issuer URL,
/// auto-discovered audience/JWKS) and refuses every request with a 503
/// that clearly indicates the implementation is pending. v1.1 swaps the
/// implementation to real JWKS-backed JWT verification without changing
/// the config schema or this type's public surface.
/// </summary>
/// <remarks>
/// Why ship a stub rather than reject the config entirely: operators
/// configuring a production deployment against v1.0 can write their
/// final <c>oidcProviders</c> config now, validate it loads cleanly and
/// auto-discovery succeeds (those happen elsewhere in the pipeline),
/// and have everything start working the moment v1.1 lands. The 503
/// surfaces the gap explicitly rather than silently behaving wrong.
/// </remarks>
public sealed class OidcValidator : IAuthValidator
{
    private readonly OidcProvider _provider;
    private readonly ILogger<OidcValidator> _logger;
    private readonly bool _loggedOnce;

    public string Name => $"oidc:{_provider.Name}";

    public OidcValidator(OidcProvider provider, ILogger<OidcValidator> logger)
    {
        _provider = provider;
        _logger = logger;
        // One-time WARN at construction so operators see the stub during
        // startup, not just at the first protected request.
        _logger.LogWarning(
            "OIDC validator constructed for provider={Provider} issuer={Issuer} — v1.0 stub, all protected requests will return 503 until v1.1.",
            _provider.Name, _provider.Issuer);
        _loggedOnce = true;
    }

    public ValueTask<AuthResult> ValidateAsync(HttpContext context)
    {
        _ = _loggedOnce;
        _logger.LogWarning(
            "OIDC validate → 503 not_implemented provider={Provider}",
            _provider.Name);
        return ValueTask.FromResult(AuthResult.Deny(
            StatusCodes.Status503ServiceUnavailable,
            "oidc_not_yet_implemented",
            "OIDC validation is not yet implemented in this platform version. " +
            "Configure auth.mode=demo for integrator testing or auth.mode=none for local dev. " +
            "OIDC support lands in v1.1; see https://github.com/ryanhebert/WinMCP/blob/main/docs/BACKLOG.md."));
    }
}
