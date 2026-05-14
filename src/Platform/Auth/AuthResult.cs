namespace WinMcp.Platform.Auth;

/// <summary>
/// Outcome of an <see cref="IAuthValidator"/> validating a request.
/// Validators decide; the wrapping <see cref="AuthMiddleware"/> turns
/// denials into HTTP responses (status code, WWW-Authenticate header,
/// JSON error body).
/// </summary>
public sealed class AuthResult
{
    public bool Allowed { get; private init; }

    /// <summary>Identity tag for the path through the validator (e.g., "static bearer", "issued bearer", "anonymous", "foreign-shaped bearer"). Used in INFO logs on allow.</summary>
    public string? AllowReason { get; private init; }

    /// <summary>HTTP status code when <see cref="Allowed"/> is false. Typically 401 or 503.</summary>
    public int StatusCode { get; private init; }

    /// <summary>RFC 6749 / 6750 error token (e.g., "invalid_token", "invalid_request").</summary>
    public string? ErrorCode { get; private init; }

    /// <summary>Human-readable error description, written to the JSON response body and the log.</summary>
    public string? ErrorDescription { get; private init; }

    /// <summary>Optional WWW-Authenticate challenge string for 401 responses. The middleware writes it verbatim.</summary>
    public string? WwwAuthenticate { get; private init; }

    public static AuthResult Allow(string reason) =>
        new() { Allowed = true, AllowReason = reason };

    public static AuthResult Deny(int statusCode, string errorCode, string description, string? wwwAuthenticate = null) =>
        new()
        {
            Allowed = false,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            ErrorDescription = description,
            WwwAuthenticate = wwwAuthenticate
        };
}
