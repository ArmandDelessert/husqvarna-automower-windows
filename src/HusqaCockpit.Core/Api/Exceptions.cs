using System.Net;

namespace HusqaCockpit.Core.Api;

/// <summary>The authentication server refused to issue a token.</summary>
public sealed class AuthenticationException(
    HttpStatusCode? statusCode,
    string? error,
    string? errorCode,
    string message,
    Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Null when the server could not be reached at all.</summary>
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>OAuth2 error, e.g. "invalid_client" or "invalid_request".</summary>
    public string? Error { get; } = error;

    /// <summary>Husqvarna-specific error code, e.g. "simultaneous.logins".</summary>
    public string? ErrorCode { get; } = errorCode;

    /// <summary>True when the credentials themselves are wrong (as opposed to a transient problem).</summary>
    public bool IsInvalidCredentials =>
        Error is "invalid_client" or "invalid_grant" or "unauthorized_client"
        || StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary>Husqvarna rejects token requests made too close together.</summary>
    public bool IsTooManyLogins => ErrorCode is "simultaneous.logins";

    public bool IsNetworkError => StatusCode is null;
}

/// <summary>The Automower Connect API answered with an error status.</summary>
public sealed class AutomowerApiException(HttpStatusCode statusCode, string? errorCode, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ErrorCode { get; } = errorCode;
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;
}
