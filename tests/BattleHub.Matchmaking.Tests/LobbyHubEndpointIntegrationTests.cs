using System.Net;
using System.Text.Json;

namespace BattleHub.Matchmaking.Tests;

public class LobbyHubEndpointIntegrationTests : IClassFixture<MatchesEndpointIntegrationTests.Factory>
{
    private readonly HttpClient _client;

    public LobbyHubEndpointIntegrationTests(MatchesEndpointIntegrationTests.Factory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Negotiate_ReturnsValidSignalRNegotiationDocument()
    {
        using var response = await _client.PostAsync(
            "/hubs/lobby/negotiate?negotiateVersion=1",
            content: null);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(json);
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("connectionId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("connectionToken").GetString()));
        Assert.NotEmpty(document.RootElement.GetProperty("availableTransports").EnumerateArray());
    }
}
