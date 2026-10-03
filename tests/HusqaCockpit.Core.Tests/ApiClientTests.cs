using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Models;
using Microsoft.Extensions.Time.Testing;

namespace HusqaCockpit.Core.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class FakeTokens : IAccessTokenProvider
{
    public int Invalidations { get; private set; }
    public string ApplicationKey => "app-key";
    public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult($"token-{Invalidations}");
    public void Invalidate() => Invalidations++;
}

internal sealed class MemoryTokenCache : ITokenCache
{
    public AccessToken? Token { get; set; }
    public AccessToken? Load() => Token;
    public void Save(AccessToken token) => Token = token;
    public void Clear() => Token = null;
}

public class TokenProviderTests
{
    private const string TokenJson = """{ "access_token": "abc", "expires_in": 86399, "scope": "iam:read", "token_type": "Bearer" }""";
    private static readonly ApiCredentials s_credentials = new("app-key", "app-secret");

    [Fact]
    public async Task Requests_a_token_with_client_credentials_and_caches_it()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, TokenJson));
        var cache = new MemoryTokenCache();
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T10:00:00Z"));
        var provider = new ClientCredentialsTokenProvider(new HttpClient(handler), s_credentials, cache, time);

        Assert.Equal("abc", await provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("abc", await provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("grant_type=client_credentials&client_id=app-key&client_secret=app-secret", body);
        Assert.Equal(time.GetUtcNow().AddSeconds(86399), cache.Token!.ExpiresAt);
    }

    [Fact]
    public async Task Reuses_a_persisted_token_after_restart()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Should not log in"));
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T10:00:00Z"));
        var cache = new MemoryTokenCache { Token = new AccessToken("app-key", "persisted", time.GetUtcNow().AddHours(5)) };
        var provider = new ClientCredentialsTokenProvider(new HttpClient(handler), s_credentials, cache, time);

        Assert.Equal("persisted", await provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ignores_a_persisted_token_from_another_application_key()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, TokenJson));
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T10:00:00Z"));
        var cache = new MemoryTokenCache { Token = new AccessToken("other-key", "persisted", time.GetUtcNow().AddHours(5)) };
        var provider = new ClientCredentialsTokenProvider(new HttpClient(handler), s_credentials, cache, time);

        Assert.Equal("abc", await provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Renews_the_token_shortly_before_expiry()
    {
        var count = 0;
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, $$"""{ "access_token": "t{{++count}}", "expires_in": 3600 }"""));
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T10:00:00Z"));
        var provider = new ClientCredentialsTokenProvider(new HttpClient(handler), s_credentials, null, time);

        Assert.Equal("t1", await provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        time.Advance(TimeSpan.FromMinutes(55));
        Assert.Equal("t2", await provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reports_simultaneous_logins()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.BadRequest, """
            { "error": "invalid_request", "error_description": "Simultaneous logins detected", "error_code": "simultaneous.logins" }
            """));
        var provider = new ClientCredentialsTokenProvider(new HttpClient(handler), s_credentials);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => provider.GetAccessTokenAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.True(ex.IsTooManyLogins);
        Assert.False(ex.IsInvalidCredentials);
        Assert.Equal("Simultaneous logins detected", ex.Message);
    }

    [Fact]
    public async Task Reports_invalid_credentials()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.BadRequest, """{ "error": "invalid_client" }"""));
        var provider = new ClientCredentialsTokenProvider(new HttpClient(handler), s_credentials);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => provider.GetAccessTokenAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.True(ex.IsInvalidCredentials);
    }
}

public class AutomowerClientTests
{
    private static AutomowerClient CreateClient(FakeHandler handler, IAccessTokenProvider? tokens = null) =>
        new(new HttpClient(handler), tokens ?? new FakeTokens(), minimumRequestInterval: TimeSpan.Zero);

    [Fact]
    public async Task Sends_the_required_headers()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, TestData.MowersJson));

        var resources = await CreateClient(handler).GetMowerResourcesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, resources.Count);
        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal("https://api.amc.husqvarna.dev/v1/mowers", request.RequestUri!.ToString());
        Assert.Equal("Bearer token-0", request.Headers.Authorization!.ToString());
        Assert.Equal("app-key", request.Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("husqvarna", request.Headers.GetValues("Authorization-Provider").Single());
    }

    public static TheoryData<string, string> Actions => new()
    {
        { "start", """{"data":{"type":"Start","attributes":{"duration":90}}}""" },
        { "pause", """{"data":{"type":"Pause"}}""" },
        { "resume", """{"data":{"type":"ResumeSchedule"}}""" },
        { "park", """{"data":{"type":"Park","attributes":{"duration":180}}}""" },
        { "parkNext", """{"data":{"type":"ParkUntilNextSchedule"}}""" },
        { "parkForever", """{"data":{"type":"ParkUntilFurtherNotice"}}""" },
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Actions_are_sent_as_json_api_documents(string action, string expectedBody)
    {
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        var command = action switch
        {
            "start" => MowerAction.Start(TimeSpan.FromMinutes(90)),
            "pause" => MowerAction.Pause(),
            "resume" => MowerAction.ResumeSchedule(),
            "park" => MowerAction.Park(TimeSpan.FromHours(3)),
            "parkNext" => MowerAction.ParkUntilNextSchedule(),
            _ => MowerAction.ParkUntilFurtherNotice(),
        };

        await CreateClient(handler).SendActionAsync("m-1", command, TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.amc.husqvarna.dev/v1/mowers/m-1/actions", request.RequestUri!.ToString());
        Assert.Equal("application/vnd.api+json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(expectedBody, body);
    }

    [Fact]
    public async Task Settings_are_sent_as_json_api_documents()
    {
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        var client = CreateClient(handler);

        await client.SetCuttingHeightAsync("m-1", 4, TestContext.Current.CancellationToken);
        await client.SetHeadlightModeAsync("m-1", HeadlightMode.EveningOnly, TestContext.Current.CancellationToken);

        Assert.Equal("""{"data":{"type":"settings","attributes":{"cuttingHeight":4}}}""", handler.Requests[0].Body);
        Assert.Equal("""{"data":{"type":"settings","attributes":{"headlight":{"mode":"EVENING_ONLY"}}}}""", handler.Requests[1].Body);
        Assert.EndsWith("/mowers/m-1/settings", handler.Requests[1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Calendar_is_sent_with_all_days()
    {
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));

        await CreateClient(handler).SetCalendarAsync("m-1", [new CalendarTask { Start = 480, Duration = 120, Monday = true }], TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(handler.Requests[0].Body!)!;
        Assert.Equal("calendar", body["data"]!["type"]!.GetValue<string>());
        var task = body["data"]!["attributes"]!["tasks"]![0]!;
        Assert.Equal(480, task["start"]!.GetValue<int>());
        Assert.True(task["monday"]!.GetValue<bool>());
        Assert.False(task["sunday"]!.GetValue<bool>());
        Assert.Equal(
            ["duration", "friday", "monday", "saturday", "start", "sunday", "thursday", "tuesday", "wednesday"],
            task.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Retries_once_with_a_new_token_after_401()
    {
        var tokens = new FakeTokens();
        var handler = new FakeHandler((request, _) =>
            request.Headers.Authorization!.Parameter == "token-0"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : FakeHandler.Json(HttpStatusCode.OK, """{"data":[]}"""));

        var resources = await CreateClient(handler, tokens).GetMowerResourcesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(resources);
        Assert.Equal(1, tokens.Invalidations);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Api_errors_carry_the_server_detail()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.BadRequest, """
            { "errors": [ { "id": "x", "status": "400", "code": "invalid.cutting.height", "title": "Bad", "detail": "Cutting height must be 1-9" } ] }
            """));

        var ex = await Assert.ThrowsAsync<AutomowerApiException>(
            () => CreateClient(handler).SetCuttingHeightAsync("m-1", 42, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("invalid.cutting.height", ex.ErrorCode);
        Assert.Equal("Cutting height must be 1-9", ex.Message);
    }

    [Fact]
    public async Task Messages_are_parsed()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, """
            { "data": { "type": "messages", "id": "messages", "attributes": { "messages": [
                { "time": 1751146587, "code": 2, "severity": "ERROR", "latitude": 49, "longitude": 10 } ] } } }
            """));

        var messages = await CreateClient(handler).GetMessagesAsync("m-1", TestContext.Current.CancellationToken);

        var message = Assert.Single(messages);
        Assert.Equal(2, message.Code);
        Assert.Equal(MessageSeverity.Error, message.Severity);
    }

    [Fact]
    public async Task Empty_message_list_is_handled()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, """{"data":{"type":"messages","id":"messages","attributes":{}}}"""));

        Assert.Empty(await CreateClient(handler).GetMessagesAsync("m-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Requests_are_spaced_by_the_minimum_interval()
    {
        var time = new FakeTimeProvider();
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, """{"data":[]}"""));
        var client = new AutomowerClient(new HttpClient(handler), new FakeTokens(), time, minimumRequestInterval: TimeSpan.FromSeconds(1));

        await client.GetMowerResourcesAsync(TestContext.Current.CancellationToken);
        var second = client.GetMowerResourcesAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(handler.Requests);

        time.Advance(TimeSpan.FromSeconds(1));
        await second;
        Assert.Equal(2, handler.Requests.Count);
    }
}
