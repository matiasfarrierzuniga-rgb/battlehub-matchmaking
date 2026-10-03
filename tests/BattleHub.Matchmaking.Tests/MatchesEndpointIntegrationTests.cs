using System.Net;
using System.Net.Http.Json;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Transport.Matches;
using BattleHub.Matchmaking.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BattleHub.Matchmaking.Tests;

public class MatchesEndpointIntegrationTests : IClassFixture<MatchesEndpointIntegrationTests.Factory>
{
    private readonly HttpClient _client;

    public MatchesEndpointIntegrationTests(Factory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealth_RemainsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetMatches_WithFakeStore_ReturnsOkAndTextStatusWithUtcDate()
    {
        var response = await _client.GetAsync("/api/matches");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Waiting\"", json);
        Assert.DoesNotContain("\"status\":0", json);
        Assert.Contains("\"createdAt\":\"2026-10-02T18:00:00Z\"", json);
        Assert.DoesNotContain("+00:00", json);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetMissingMatch_ReturnsNotFoundProblemDetails()
    {
        var response = await _client.GetAsync("/api/matches/missing");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"status\":404", json);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetMatches_WithInvalidStatus_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/matches?status=invalid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostMatch_WithoutIdentity_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/matches",
            new { title = "Friday", gameType = "Trivia", maxPlayers = 4 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("join", "POST")]
    [InlineData("leave", "POST")]
    [InlineData("start", "POST")]
    [InlineData("", "DELETE")]
    [Trait("Category", "Integration")]
    public async Task ProtectedMatchActions_WithoutIdentity_ReturnUnauthorized(string action, string method)
    {
        var suffix = string.IsNullOrEmpty(action) ? string.Empty : $"/{action}";
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/matches/match-1{suffix}");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(TestAuthHandler.NameIdentifierHeader, "name-user")]
    [InlineData(TestAuthHandler.SubHeader, "sub-user")]
    [Trait("Category", "Integration")]
    public async Task PostMatch_UsesAuthenticatedPrincipalIdentity(string header, string expectedUserId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matches")
        {
            Content = JsonContent.Create(new { title = "Authenticated", gameType = "Trivia", maxPlayers = 4 })
        };
        request.Headers.Add(header, expectedUserId);

        using var response = await _client.SendAsync(request);
        var match = await response.Content.ReadFromJsonAsync<MatchResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expectedUserId, match!.CreatedBy);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostMatch_WhenBothClaimsExist_PrefersNameIdentifier()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matches")
        {
            Content = JsonContent.Create(new { title = "Claim priority", gameType = "Trivia", maxPlayers = 4 })
        };
        request.Headers.Add(TestAuthHandler.NameIdentifierHeader, "name-user");
        request.Headers.Add(TestAuthHandler.SubHeader, "sub-user");

        using var response = await _client.SendAsync(request);
        var match = await response.Content.ReadFromJsonAsync<MatchResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("name-user", match!.CreatedBy);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AuthenticatedActors_AreUsedForJoinStartAndCancel()
    {
        var created = await CreateAuthenticatedMatch("owner-user", "Actor operations");

        using var join = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{created.Id}/join");
        join.Headers.Add(TestAuthHandler.SubHeader, "participant-user");
        using var joinResponse = await _client.SendAsync(join);
        var joined = await joinResponse.Content.ReadFromJsonAsync<MatchResponse>();

        Assert.Equal(HttpStatusCode.OK, joinResponse.StatusCode);
        Assert.Equal(2, joined!.CurrentPlayers);

        using var start = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{created.Id}/start");
        start.Headers.Add(TestAuthHandler.NameIdentifierHeader, "owner-user");
        using var startResponse = await _client.SendAsync(start);
        var started = await startResponse.Content.ReadFromJsonAsync<MatchResponse>();

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Equal("Started", started!.Status);

        var cancellable = await CreateAuthenticatedMatch("cancel-owner", "Cancellation");
        using var cancel = new HttpRequestMessage(HttpMethod.Delete, $"/api/matches/{cancellable.Id}");
        cancel.Headers.Add(TestAuthHandler.NameIdentifierHeader, "cancel-owner");
        using var cancelResponse = await _client.SendAsync(cancel);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<MatchResponse>();

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal("Cancelled", cancelled!.Status);
    }

    private async Task<MatchResponse> CreateAuthenticatedMatch(string owner, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matches")
        {
            Content = JsonContent.Create(new { title, gameType = "Trivia", maxPlayers = 4 })
        };
        request.Headers.Add(TestAuthHandler.NameIdentifierHeader, owner);

        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchResponse>())!;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Auth0:Domain"] = "test.auth0.invalid",
                    ["Auth0:Audience"] = "test-matchmaking-api"
                }));

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                        options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.AuthenticationScheme, _ => { });
                services.RemoveAll<IMatchStore>();
                var store = new TestMatchStore();
                var createdAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(-6));
                store.Add(new Match("match-1", "Friday", "Trivia", "owner-1", createdAt, 4));
                services.AddSingleton<IMatchStore>(store);
            });
        }
    }
}
