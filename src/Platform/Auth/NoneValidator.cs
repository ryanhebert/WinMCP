using Microsoft.AspNetCore.Http;

namespace WinMcp.Platform.Auth;

/// <summary>
/// Allows every request unconditionally. Used when an auth domain is in
/// <c>AuthMode.None</c> (local dev, or routes intentionally opened to the
/// network). Operators see "auth check → allow (none mode)" in the request
/// log so it's visible that the request was not authenticated.
/// </summary>
public sealed class NoneValidator : IAuthValidator
{
    public string Name => "none";

    public ValueTask<AuthResult> ValidateAsync(HttpContext context) =>
        ValueTask.FromResult(AuthResult.Allow("none mode"));
}
