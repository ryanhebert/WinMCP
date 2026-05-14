using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WinMcp.Platform.Auth;

/// <summary>
/// Validates a request against the platform's demo-mode credentials.
/// Carries forward the math-mcp v1.0.24 logic intact:
/// <list type="bullet">
///   <item><description>No <c>Authorization</c> header → allow (anonymous mixed-mode)</description></item>
///   <item><description>Non-Bearer scheme → 401 invalid_request</description></item>
///   <item><description>Bearer with our prefix (<c>mm_st_</c> / <c>mm_at_</c>) → strict validation; wrong value → 401 invalid_token</description></item>
///   <item><description>Bearer with any other shape → allow as anonymous (foreign-shaped bearer, logged at WARN)</description></item>
/// </list>
/// The prefix-aware fall-through is what restores upstream "auth none"
/// proxy routes that inject the client's identity JWT for backend auth —
/// see the v1.0.24 changelog for the Cisco Secure Access regression that
/// motivated it.
/// </summary>
public sealed class DemoValidator : IAuthValidator
{
    private readonly string? _staticBearerToken;
    private readonly TokenStore _tokenStore;
    private readonly ILogger<DemoValidator> _logger;

    public string Name => "demo";

    public DemoValidator(string? staticBearerToken, TokenStore tokenStore, ILogger<DemoValidator> logger)
    {
        _staticBearerToken = staticBearerToken;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public ValueTask<AuthResult> ValidateAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header))
        {
            return ValueTask.FromResult(AuthResult.Allow("anonymous"));
        }

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Token check → 401 malformed (non-Bearer scheme): header={Preview}",
                Truncate(header));
            return ValueTask.FromResult(AuthResult.Deny(
                StatusCodes.Status401Unauthorized,
                "invalid_request",
                "malformed Authorization header",
                BuildChallenge(context, "invalid_request", "malformed Authorization header")));
        }

        var presented = header.Substring("Bearer ".Length).Trim();

        // Prefix-aware: only enforce strict 401 for bearers shaped like ours.
        // Anything else is a proxy-injected foreign token (Cisco SSE user
        // JWT, Cloudflare Access service token, etc.) and falls through as
        // anonymous. See v1.0.24 changelog for context.
        var isOurs = presented.StartsWith(CredentialGenerator.StaticBearerPrefix, StringComparison.Ordinal)
                  || presented.StartsWith(CredentialGenerator.IssuedTokenPrefix, StringComparison.Ordinal);
        if (!isOurs)
        {
            _logger.LogWarning(
                "Token check → allow (foreign-shaped bearer, treating as anonymous) presented={Preview}",
                Truncate(presented));
            return ValueTask.FromResult(AuthResult.Allow("foreign-shaped bearer (anonymous)"));
        }

        var staticToken = _staticBearerToken ?? string.Empty;
        if (!string.IsNullOrEmpty(staticToken) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(staticToken)))
        {
            return ValueTask.FromResult(AuthResult.Allow("static bearer"));
        }

        if (_tokenStore.IsValid(presented))
        {
            return ValueTask.FromResult(AuthResult.Allow("issued bearer"));
        }

        _logger.LogWarning(
            "Token check → 401 invalid bearer presented={Preview}",
            Truncate(presented));
        return ValueTask.FromResult(AuthResult.Deny(
            StatusCodes.Status401Unauthorized,
            "invalid_token",
            "bearer token not recognized",
            BuildChallenge(context, "invalid_token", "bearer token not recognized")));
    }

    private static string BuildChallenge(HttpContext context, string error, string detail)
    {
        var origin = $"{context.Request.Scheme}://{context.Request.Host.Value}";
        var resourceMetadata = $"{origin}/.well-known/oauth-protected-resource";
        var safeDetail = detail.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return
            $"Bearer realm=\"WinMCP\"" +
            $", error=\"{error}\"" +
            $", error_description=\"{safeDetail}\"" +
            $", resource_metadata=\"{resourceMetadata}\"";
    }

    private static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        var head = s.Length <= 10 ? s : string.Concat(s.AsSpan(0, 10), "...");
        return $"{head} (len={s.Length})";
    }
}
