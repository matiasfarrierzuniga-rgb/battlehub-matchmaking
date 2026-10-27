using System.Net;
using System.Net.Http.Json;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Infrastructure.Profiles;
using BattleHub.Matchmaking.Api.Transport.Matches;
using BattleHub.Matchmaking.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BattleHub.Matchmaking.Tests;

public class MatchHistoryEndpointTests : IClassFixture<MatchHistoryEndpointTests.Factory>
{
    private const string TypingClient = "8BWcE4T8HxhJrxU1CtgmNkjDOpkrN4Su";
    private const string TriviaClient = "xYhNYG9lOGLA6KvEM0Y4cccH2f7JDD5x";
    private const string MemoryClient = "lfcBlgOCs9N0w6F6AWl4FqOs4xF9AQiN";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public MatchHistoryEndpointTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task History_WithoutIdentity_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync("/api/matches/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task History_WithInvalidToken_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/matches/history");
        request.Headers.Add("Authorization", "Bearer not-a-jwt");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task History_WithoutMatches_ReturnsEmptyPage()
    {
        var page = await History("nobody");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(MatchHistory.DefaultPageSize, page.PageSize);
    }

    [Theory]
    [InlineData("typing", TypingClient, 4)]
    [InlineData("trivia", TriviaClient, 4)]
    [InlineData("memory", MemoryClient, 2)]
    [Trait("Category", "Integration")]
    public async Task History_AfterFinish_IsVisibleToParticipantsOnly(
        string gameType,
        string clientId,
        int maxPlayers)
    {
        var host = $"{gameType}-host";
        var guest = $"{gameType}-guest";
        var match = await Play(host, guest, gameType, maxPlayers, clientId);

        var hostPage = await History(host, $"gameType={gameType}");
        var guestPage = await History(guest, $"gameType={gameType}");
        var outsider = await History("outsider", $"gameType={gameType}");
        var hostItem = Assert.Single(hostPage.Items, item => item.MatchId == match.Id);
        var guestItem = Assert.Single(guestPage.Items, item => item.MatchId == match.Id);

        Assert.Equal(gameType, hostItem.GameType);
        Assert.Equal("Finished", hostItem.Status);
        Assert.Equal(maxPlayers, hostItem.MaxPlayers);
        Assert.Equal(2, hostItem.ParticipantCount);
        Assert.True(hostItem.IsHost);
        Assert.False(guestItem.IsHost);
        Assert.NotNull(hostItem.StartedAt);
        Assert.NotNull(hostItem.FinishedAt);
        Assert.Null(hostItem.CancelledAt);
        Assert.DoesNotContain("userId", await ReadBody(host), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(outsider.Items, item => item.MatchId == match.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task History_IgnoresARequestedUserId()
    {
        var match = await Play("owner-a", "guest-a", "typing", 4, TypingClient);

        var page = await History("owner-a", "userId=guest-a");

        Assert.Contains(page.Items, item => item.MatchId == match.Id && item.IsHost);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task History_OmitsWaitingAndCancelledRoomsThatNeverStarted()
    {
        var created = await Create("waiting-host", "Still waiting", "trivia", 4);
        using var cancel = User(HttpMethod.Delete, $"/api/matches/{created.Id}", "waiting-host");
        using var cancelResponse = await _client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var page = await History("waiting-host");

        Assert.DoesNotContain(page.Items, item => item.MatchId == created.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task History_IsNewestFirstAndPaged()
    {
        _factory.Store.Now = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
        var older = await Play("pager", "pager-guest", "typing", 4, TypingClient);
        _factory.Store.Now = _factory.Store.Now.AddHours(2);
        var newer = await Play("pager", "pager-guest-2", "trivia", 4, TriviaClient);

        var first = await History("pager", "page=1&pageSize=1");
        var second = await History("pager", "page=2&pageSize=1");
        var ids = first.Items.Select(item => item.MatchId)
            .Concat(second.Items.Select(item => item.MatchId))
            .ToArray();

        Assert.Equal(newer.Id, first.Items[0].MatchId);
        Assert.Equal(older.Id, second.Items[0].MatchId);
        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(first.TotalCount >= 2);
    }

    [Theory]
    [InlineData("typing", TypingClient, 4)]
    [InlineData("trivia", TriviaClient, 4)]
    [InlineData("memory", MemoryClient, 2)]
    [Trait("Category", "Integration")]
    public async Task History_FiltersByGameType(string gameType, string clientId, int maxPlayers)
    {
        var host = $"{gameType}-filter-host";
        await Play(host, $"{gameType}-filter-guest", gameType, maxPlayers, clientId);

        var page = await History(host, $"gameType={gameType}");

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item => Assert.Equal(gameType, item.GameType));
    }

    [Theory]
    [InlineData("chess")]
    [InlineData("")]
    [InlineData(" ")]
    [Trait("Category", "Integration")]
    public async Task History_WithUnsupportedGameType_ReturnsBadRequest(string gameType)
    {
        using var request = User(HttpMethod.Get, $"/api/matches/history?gameType={Uri.EscapeDataString(gameType)}", "pager");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task History_WithExcessivePageSize_ReturnsBadRequest()
    {
        using var request = User(HttpMethod.Get, "/api/matches/history?pageSize=1000000", "pager");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<MatchResponse> Play(
        string host,
        string guest,
        string gameType,
        int maxPlayers,
        string clientId)
    {
        var created = await Create(host, $"{gameType} history", gameType, maxPlayers);
        using var join = User(HttpMethod.Post, $"/api/matches/{created.Id}/join", guest);
        using var joinResponse = await _client.SendAsync(join);
        Assert.Equal(HttpStatusCode.OK, joinResponse.StatusCode);
        using var start = User(HttpMethod.Post, $"/api/matches/{created.Id}/start", host);
        using var startResponse = await _client.SendAsync(start);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        using var finish = Machine($"/api/matches/{created.Id}/finish", clientId);
        using var finishResponse = await _client.SendAsync(finish);
        Assert.Equal(HttpStatusCode.NoContent, finishResponse.StatusCode);
        return created;
    }

    private async Task<MatchResponse> Create(string owner, string title, string gameType, int maxPlayers)
    {
        using var request = User(HttpMethod.Post, "/api/matches", owner);
        request.Content = JsonContent.Create(new { title, gameType, maxPlayers });
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchResponse>())!;
    }

    private async Task<HistoryPage> History(string userId, string? query = null)
    {
        var path = string.IsNullOrWhiteSpace(query) ? "/api/matches/history" : $"/api/matches/history?{query}";
        using var request = User(HttpMethod.Get, path, userId);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HistoryPage>())!;
    }

    private async Task<string> ReadBody(string userId)
    {
        using var request = User(HttpMethod.Get, "/api/matches/history", userId);
        using var response = await _client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    private static HttpRequestMessage User(HttpMethod method, string path, string userId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthHandler.NameIdentifierHeader, userId);
        return request;
    }

    private static HttpRequestMessage Machine(string path, string clientId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(TestAuthHandler.SubHeader, $"{clientId}@clients");
        request.Headers.Add(TestAuthHandler.GtyHeader, "client-credentials");
        request.Headers.Add(TestAuthHandler.AzpHeader, clientId);
        request.Headers.Add(TestAuthHandler.ScopeHeader, "matches.finish");
        return request;
    }

    private sealed record HistoryPage(HistoryItem[] Items, int Page, int PageSize, int TotalCount);

    private sealed record HistoryItem(
        string MatchId,
        string GameType,
        string Status,
        int MaxPlayers,
        int ParticipantCount,
        bool IsHost,
        DateTime CreatedAt,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        DateTime? CancelledAt);

    public sealed class Factory : WebApplicationFactory<Program>
    {
        internal TestMatchStore Store { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Cleanup:Enabled"] = "false",
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
                services.RemoveAll<IProfileDirectory>();
                services.AddSingleton<IProfileDirectory>(new GrantingProfileDirectory());
                services.RemoveAll<IMatchStore>();
                services.AddSingleton<IMatchStore>(Store);
                services.RemoveAll<IMatchHistoryStore>();
                services.AddSingleton<IMatchHistoryStore>(new MemoryMatchHistoryStore());
            });
        }
    }
}
