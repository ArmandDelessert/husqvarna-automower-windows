using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HusqaCockpit.Core.Json;
using HusqaCockpit.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HusqaCockpit.Core.Api;

/// <summary>Where <see cref="Fleet.FleetMonitor"/> reads the full state of the mowers (GET /mowers).</summary>
public interface IMowerSnapshotSource
{
    /// <summary>Returns the raw JSON:API resources ({ id, type, attributes }) of all mowers on the account.</summary>
    Task<IReadOnlyList<JsonObject>> GetMowerResourcesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// REST client for the Automower Connect API v1.
/// Requests are serialized and spaced by at least one second to respect the API rate limit.
/// </summary>
public sealed partial class AutomowerClient : IMowerSnapshotSource, IDisposable
{
    public const string DefaultBaseAddress = "https://api.amc.husqvarna.dev/v1/";
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly IAccessTokenProvider _tokens;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly RequestThrottle _throttle;
    private readonly Uri _baseAddress;

    public AutomowerClient(
        HttpClient http,
        IAccessTokenProvider tokens,
        TimeProvider? time = null,
        ILogger<AutomowerClient>? logger = null,
        string baseAddress = DefaultBaseAddress,
        TimeSpan? minimumRequestInterval = null)
    {
        _http = http;
        _tokens = tokens;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AutomowerClient>.Instance;
        _baseAddress = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/");
        _throttle = new RequestThrottle(minimumRequestInterval ?? TimeSpan.FromMilliseconds(1100), _time);
    }

    /// <summary>Raised once per HTTP request actually sent (useful to track the monthly quota).</summary>
    public event EventHandler? RequestSent;

    public void Dispose() => _throttle.Dispose();

    /// <summary>Returns the raw JSON:API resources ({ id, type, attributes }) of all mowers on the account.</summary>
    public async Task<IReadOnlyList<JsonObject>> GetMowerResourcesAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, "mowers", body: null, cancellationToken).ConfigureAwait(false);
        return root?["data"] is JsonArray data
            ? data.OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToList()
            : [];
    }

    public async Task<IReadOnlyList<MowerMessage>> GetMessagesAsync(string mowerId, CancellationToken cancellationToken = default)
    {
        var root = await SendAsync(HttpMethod.Get, $"mowers/{Escape(mowerId)}/messages", body: null, cancellationToken)
            .ConfigureAwait(false);
        var messages = root?["data"]?["attributes"]?["messages"];
        return messages?.Deserialize<List<MowerMessage>>(AutomowerJson.Options) ?? [];
    }

    public Task SendActionAsync(string mowerId, MowerAction action, CancellationToken cancellationToken = default)
    {
        var data = new JsonObject { ["type"] = action.Type };
        if (action.DurationMinutes is { } minutes)
        {
            data["attributes"] = new JsonObject { ["duration"] = minutes };
        }
        return SendAsync(HttpMethod.Post, $"mowers/{Escape(mowerId)}/actions", Wrap(data), cancellationToken);
    }

    public Task SetCuttingHeightAsync(string mowerId, int cuttingHeight, CancellationToken cancellationToken = default) =>
        SendSettingsAsync(mowerId, new JsonObject { ["cuttingHeight"] = cuttingHeight }, cancellationToken);

    public Task SetHeadlightModeAsync(string mowerId, HeadlightMode mode, CancellationToken cancellationToken = default)
    {
        var wireValue = JsonSerializer.SerializeToNode(mode, AutomowerJson.Options);
        return SendSettingsAsync(mowerId, new JsonObject { ["headlight"] = new JsonObject { ["mode"] = wireValue } }, cancellationToken);
    }

    public Task SetCalendarAsync(string mowerId, IReadOnlyList<CalendarTask> tasks, CancellationToken cancellationToken = default)
    {
        var data = new JsonObject
        {
            ["type"] = "calendar",
            ["attributes"] = new JsonObject { ["tasks"] = JsonSerializer.SerializeToNode(tasks, AutomowerJson.Options) },
        };
        return SendAsync(HttpMethod.Post, $"mowers/{Escape(mowerId)}/calendar", Wrap(data), cancellationToken);
    }

    public Task ResetCuttingBladeUsageTimeAsync(string mowerId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"mowers/{Escape(mowerId)}/statistics/resetCuttingBladeUsageTime", body: null, cancellationToken);

    public Task ConfirmErrorAsync(string mowerId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"mowers/{Escape(mowerId)}/errors/confirm", body: null, cancellationToken);

    private Task<JsonNode?> SendSettingsAsync(string mowerId, JsonObject attributes, CancellationToken cancellationToken)
    {
        var data = new JsonObject { ["type"] = "settings", ["attributes"] = attributes };
        return SendAsync(HttpMethod.Post, $"mowers/{Escape(mowerId)}/settings", Wrap(data), cancellationToken);
    }

    private static JsonObject Wrap(JsonObject data) => new() { ["data"] = data };

    private static string Escape(string id) => Uri.EscapeDataString(id);

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        var refreshedToken = false;
        for (var attempt = 1; ; attempt++)
        {
            var token = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Api-Key", _tokens.ApplicationKey);
            request.Headers.Add("Authorization-Provider", "husqvarna");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, JsonApiMediaType);
            }

            HttpResponseMessage response;
            using (await _throttle.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                LogSending(_logger, method, path);
                RequestSent?.Invoke(this, EventArgs.Empty);
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedToken)
                {
                    LogTokenRejected(_logger);
                    _tokens.Invalidate();
                    refreshedToken = true;
                    continue;
                }

                var retryable = response.StatusCode is HttpStatusCode.TooManyRequests
                    || (method == HttpMethod.Get && (int)response.StatusCode >= 500);
                if (retryable && attempt < MaxAttempts)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt);
                    LogRetrying(_logger, method, path, (int)response.StatusCode, delay);
                    await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw CreateException(response.StatusCode, text);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Method} {Path}")]
    private static partial void LogSending(ILogger logger, HttpMethod method, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Access token rejected, requesting a new one")]
    private static partial void LogTokenRejected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Method} {Path} returned {Status}, retrying in {Delay}")]
    private static partial void LogRetrying(ILogger logger, HttpMethod method, string path, int status, TimeSpan delay);

    private static AutomowerApiException CreateException(HttpStatusCode status, string body)
    {
        // JSON:API error document: { "errors": [ { "status", "code", "title", "detail" } ] }
        string? code = null;
        string? message = null;
        try
        {
            if (JsonNode.Parse(body)?["errors"] is JsonArray { Count: > 0 } errors)
            {
                code = errors[0]?["code"]?.GetValue<string>();
                message = errors[0]?["detail"]?.GetValue<string>() ?? errors[0]?["title"]?.GetValue<string>();
            }
            else
            {
                message = JsonNode.Parse(body)?["message"]?.GetValue<string>();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not JSON: fall back to the status code.
        }

        return new AutomowerApiException(status, code, message ?? $"The Automower API returned {(int)status} {status}.");
    }
}
