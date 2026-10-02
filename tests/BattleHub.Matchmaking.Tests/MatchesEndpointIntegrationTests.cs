using System.Net;
using System.Net.Http.Json;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMatchStore>();
                var store = new TestMatchStore();
                var createdAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(-6));
                store.Add(new Match("match-1", "Friday", "Trivia", "owner-1", createdAt, 4));
                services.AddSingleton<IMatchStore>(store);
            });
        }
    }
}
