using System.Net;
using System.Net.Http.Json;
using BattleHub.Matchmaking.Api.Infrastructure.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BattleHub.Matchmaking.Tests.Infrastructure.Profiles;

public class HttpProfileDirectoryTests
{
    private const string ProfileUrl = "http://localhost:5220";

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentDisplayName_ForwardsBearerToProfileMe()
    {
        var handler = new StubHandler(_ => Json(new { displayName = "  Ana  " }));
        var directory = CreateDirectory(handler);

        var displayName = await directory.GetCurrentDisplayNameAsync("Bearer user-token", default);

        Assert.Equal("Ana", displayName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("http://localhost:5220/api/profiles/me"), request.RequestUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("user-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentDisplayName_TruncatesLongNames()
    {
        var directory = CreateDirectory(new StubHandler(_ => Json(new { displayName = new string('a', 150) })));

        var displayName = await directory.GetCurrentDisplayNameAsync("Bearer user-token", default);

        Assert.Equal(100, displayName?.Length);
    }

    [Theory]
    [InlineData(null, ProfileUrl)]
    [InlineData("", ProfileUrl)]
    [InlineData("Basic dXNlcjpwYXNz", ProfileUrl)]
    [InlineData("Bearer ", ProfileUrl)]
    [InlineData("Bearer user-token", "")]
    [Trait("Category", "Unit")]
    public async Task GetCurrentDisplayName_WithoutBearerOrBaseUrl_DoesNotCallProfile(
        string? authorization,
        string baseUrl)
    {
        var handler = new StubHandler(_ => Json(new { displayName = "Ana" }));
        var directory = CreateDirectory(handler, baseUrl);

        Assert.Null(await directory.GetCurrentDisplayNameAsync(authorization, default));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [Trait("Category", "Unit")]
    public async Task GetCurrentDisplayName_WhenProfileRejects_ReturnsNull(HttpStatusCode status)
    {
        var directory = CreateDirectory(new StubHandler(_ => new HttpResponseMessage(status)));

        Assert.Null(await directory.GetCurrentDisplayNameAsync("Bearer user-token", default));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentDisplayName_WhenProfileIsUnreachable_ReturnsNull()
    {
        var directory = CreateDirectory(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        Assert.Null(await directory.GetCurrentDisplayNameAsync("Bearer user-token", default));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentDisplayName_WithBlankName_ReturnsNull()
    {
        var directory = CreateDirectory(new StubHandler(_ => Json(new { displayName = "   " })));

        Assert.Null(await directory.GetCurrentDisplayNameAsync("Bearer user-token", default));
    }

    private static HttpProfileDirectory CreateDirectory(StubHandler handler, string baseUrl = ProfileUrl) =>
        new(
            new HttpClient(handler),
            Options.Create(new ProfileOptions { BaseUrl = baseUrl }),
            NullLogger<HttpProfileDirectory>.Instance);

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
