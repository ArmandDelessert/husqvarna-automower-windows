using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HusqaCockpit.Core.Api;

public sealed record ApiCredentials(string ApplicationKey, string ApplicationSecret);

public sealed record AccessToken(string ApplicationKey, string Value, DateTimeOffset ExpiresAt);

/// <summary>Persists the last token so that restarting the app does not trigger a new login.</summary>
public interface ITokenCache
{
    AccessToken? Load();
    void Save(AccessToken token);
    void Clear();
}

public interface IAccessTokenProvider
{
    string ApplicationKey { get; }
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Forget the current token (e.g. after a 401) so the next call requests a fresh one.</summary>
    void Invalidate();
}

/// <summary>
/// OAuth2 client-credentials flow against the Husqvarna authentication API.
/// Tokens live 24 h; they are reused until shortly before expiry because the server
/// rejects logins that are too frequent ("simultaneous.logins").
/// </summary>
public sealed partial class ClientCredentialsTokenProvider : IAccessTokenProvider, IDisposable
{
    public const string DefaultTokenEndpoint = "https://api.authentication.husqvarnagroup.dev/v1/oauth2/token";
    private static readonly TimeSpan s_renewalMargin = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly ApiCredentials _credentials;
    private readonly ITokenCache? _cache;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Uri _endpoint;
    private AccessToken? _token;

    public ClientCredentialsTokenProvider(
        HttpClient http,
        ApiCredentials credentials,
        ITokenCache? cache = null,
        TimeProvider? time = null,
        ILogger<ClientCredentialsTokenProvider>? logger = null,
        string tokenEndpoint = DefaultTokenEndpoint)
    {
        _http = http;
        _credentials = credentials;
        _cache = cache;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ClientCredentialsTokenProvider>.Instance;
        _endpoint = new Uri(tokenEndpoint);
    }

    public string ApplicationKey => _credentials.ApplicationKey;

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsUsable(_token))
        {
            return _token!.Value;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsUsable(_token))
            {
                return _token!.Value;
            }

            var cached = _cache?.Load();
            if (cached is not null && cached.ApplicationKey == _credentials.ApplicationKey && IsUsable(cached))
            {
                LogReusingCachedToken(_logger, cached.ExpiresAt);
                _token = cached;
                return cached.Value;
            }

            _token = await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
            _cache?.Save(_token);
            return _token.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _token = null;
        _cache?.Clear();
    }

    public void Dispose() => _gate.Dispose();

    private bool IsUsable(AccessToken? token) =>
        token is not null && token.ExpiresAt - s_renewalMargin > _time.GetUtcNow();

    private async Task<AccessToken> RequestTokenAsync(CancellationToken cancellationToken)
    {
        LogRequestingToken(_logger);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _credentials.ApplicationKey,
            ["client_secret"] = _credentials.ApplicationSecret,
        });

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AuthenticationException(null, null, null, $"Cannot reach the authentication server: {ex.Message}", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryParse<TokenError>(body);
                LogTokenRequestFailed(_logger, (int)response.StatusCode, error?.Error, error?.ErrorCode);
                throw new AuthenticationException(
                    response.StatusCode,
                    error?.Error,
                    error?.ErrorCode,
                    error?.ErrorDescription ?? $"Authentication failed ({(int)response.StatusCode}).");
            }

            var token = TryParse<TokenResponse>(body);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new AuthenticationException(response.StatusCode, null, null, "The authentication server returned no token.");
            }

            var lifetime = TimeSpan.FromSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600);
            return new AccessToken(_credentials.ApplicationKey, token.AccessToken, _time.GetUtcNow() + lifetime);
        }
    }

    private static T? TryParse<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reusing cached access token valid until {ExpiresAt}")]
    private static partial void LogReusingCachedToken(ILogger logger, DateTimeOffset expiresAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Requesting a new access token")]
    private static partial void LogRequestingToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token request failed: {Status} {Error} {ErrorCode}")]
    private static partial void LogTokenRequestFailed(ILogger logger, int status, string? error, string? errorCode);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);

    private sealed record TokenError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_code")] string? ErrorCode,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
