using Microsoft.AspNetCore.Http;

namespace WinMcp.Platform.Auth;

/// <summary>
/// Validates an HTTP request against an auth domain's policy and returns
/// an <see cref="AuthResult"/>. The wrapping <see cref="AuthMiddleware"/>
/// applies the result — allow continues the pipeline; deny writes the
/// appropriate HTTP response.
/// </summary>
/// <remarks>
/// Implementations are kept per-(domain, config) instance and injected
/// into the middleware via <see cref="AuthMiddlewareFactory"/>. Reuse a
/// single instance across requests; the validator is responsible for any
/// internal thread-safety (e.g., <see cref="TokenStore"/> uses
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>).
/// </remarks>
public interface IAuthValidator
{
    /// <summary>
    /// Short human-readable label of the validator (e.g., "none", "demo",
    /// "oidc:corp-okta"). Used in startup logs and on dashboard.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Validate the incoming request. Implementations must be fast — this
    /// runs synchronously on every protected request. Async is on the
    /// signature only to accommodate future OIDC validators that may
    /// refresh JWKS over the network.
    /// </summary>
    ValueTask<AuthResult> ValidateAsync(HttpContext context);
}
