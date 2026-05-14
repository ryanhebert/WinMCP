using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WinMcp.Platform.Auth;

/// <summary>
/// Pipeline middleware that delegates to an <see cref="IAuthValidator"/> and
/// turns the resulting <see cref="AuthResult"/> into either a continuation
/// of the pipeline (allow) or an RFC-compliant 4xx/5xx response (deny).
/// </summary>
/// <remarks>
/// One instance per (domain, validator) pair. Registered via:
/// <code>app.UseWhen(path-predicate, b =&gt; b.UseMiddleware&lt;AuthMiddleware&gt;(validator));</code>
/// The <c>validator</c> argument is appended after the framework-injected
/// <c>RequestDelegate</c> and <c>ILogger</c> dependencies in the constructor.
/// </remarks>
public sealed class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuthValidator _validator;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger, IAuthValidator validator)
    {
        _next = next;
        _logger = logger;
        _validator = validator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var result = await _validator.ValidateAsync(context);
        if (result.Allowed)
        {
            _logger.LogInformation(
                "Auth check on {Path} → allow ({Reason}) via {Validator}",
                context.Request.Path, result.AllowReason, _validator.Name);
            await _next(context);
            return;
        }

        await WriteDenialAsync(context, result);
    }

    private static async Task WriteDenialAsync(HttpContext context, AuthResult result)
    {
        context.Response.StatusCode = result.StatusCode;
        context.Response.ContentType = "application/json";
        if (!string.IsNullOrEmpty(result.WwwAuthenticate))
        {
            context.Response.Headers.WWWAuthenticate = result.WwwAuthenticate;
        }

        var body = new
        {
            error = result.ErrorCode,
            error_description = result.ErrorDescription
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
