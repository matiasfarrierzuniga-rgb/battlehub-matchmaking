using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BattleHub.Matchmaking.Tests;

public class HealthEndpointIntegrationTests : IClassFixture<MatchesEndpointIntegrationTests.Factory>
{
    private readonly HttpClient _client;

    public HealthEndpointIntegrationTests(MatchesEndpointIntegrationTests.Factory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealth_ReturnsOkStatus()
    {
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", content);
    }
}
