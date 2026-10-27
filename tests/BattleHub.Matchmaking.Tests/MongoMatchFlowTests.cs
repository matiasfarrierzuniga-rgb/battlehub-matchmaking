using System.Net;
using System.Net.Http.Json;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Infrastructure.MongoDb;
using BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;
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
using MongoDB.Driver;

namespace BattleHub.Matchmaking.Tests;

public class MongoMatchFlowTests : IClassFixture<MongoMatchFlowTests.Factory>
{
    private const string DatabaseName = "battlehub_matchmaking_itest";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public MongoMatchFlowTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Mongo")]
    public void Store_IsTheRealMongoImplementation()
    {
        Assert.IsType<MongoMatchStore>(_factory.Services.GetRequiredService<IMatchStore>());
    }

    [Fact]
    [Trait("Category", "Mongo")]
    public async Task CreateJoinAndStart_ArePersistedInMongo()
    {
        var created = await Create("mongo-host", "Mongo flow", "typing");
        using (var join = Authorized(HttpMethod.Post, $"/api/matches/{created.Id}/join", "mongo-guest"))
        using (var joinResponse = await _client.SendAsync(join))
        {
            Assert.Equal(HttpStatusCode.OK, joinResponse.StatusCode);
        }

        using var start = Authorized(HttpMethod.Post, $"/api/matches/{created.Id}/start", "mongo-host");
        using var startResponse = await _client.SendAsync(start);
        var started = await startResponse.Content.ReadFromJsonAsync<MatchResponse>();

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Equal("Started", started!.Status);

        var document = await Load(created.Id);
        Assert.Equal(MatchStatus.Started, document.Status);
        Assert.Equal(["mongo-host", "mongo-guest"], document.Participants.Select(player => player.UserId));
    }

    [Fact]
    [Trait("Category", "Mongo")]
    public async Task Start_WithOnlyTheOwner_StaysWaitingInMongo()
    {
        var created = await Create("mongo-solo", "Mongo solo", "trivia");

        using var start = Authorized(HttpMethod.Post, $"/api/matches/{created.Id}/start", "mongo-solo");
        using var startResponse = await _client.SendAsync(start);

        Assert.Equal(HttpStatusCode.Conflict, startResponse.StatusCode);
        var document = await Load(created.Id);
        Assert.Equal(MatchStatus.Waiting, document.Status);
        Assert.Equal("mongo-solo", Assert.Single(document.Participants).UserId);
    }

    [Fact]
    [Trait("Category", "Mongo")]
    public async Task FinishedMatch_IsListedForParticipantsInMongoHistory()
    {
        Assert.IsType<MongoMatchHistoryStore>(_factory.Services.GetRequiredService<IMatchHistoryStore>());
        var created = await Create("history-host", "Mongo history", "typing");
        using (var join = Authorized(HttpMethod.Post, $"/api/matches/{created.Id}/join", "history-guest"))
        using (var joinResponse = await _client.SendAsync(join))
        {
            Assert.Equal(HttpStatusCode.OK, joinResponse.StatusCode);
        }

        using (var start = Authorized(HttpMethod.Post, $"/api/matches/{created.Id}/start", "history-host"))
        using (var startResponse = await _client.SendAsync(start))
        {
            Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        }

        using var finish = Machine(created.Id);
        using var finishResponse = await _client.SendAsync(finish);
        Assert.Equal(HttpStatusCode.NoContent, finishResponse.StatusCode);

        var document = await Load(created.Id);
        Assert.Equal(MatchStatus.Finished, document.Status);
        Assert.NotNull(document.StartedAt);
        Assert.NotNull(document.FinishedAt);
        Assert.Null(document.CancelledAt);

        var host = await History("history-host");
        var guest = await History("history-guest");
        var outsider = await History("history-outsider");

        Assert.Contains(host.Items, item => item.MatchId == created.Id && item.IsHost && item.GameType == "typing");
        Assert.Contains(guest.Items, item => item.MatchId == created.Id && !item.IsHost);
        Assert.DoesNotContain(outsider.Items, item => item.MatchId == created.Id);
    }

    private async Task<HistoryPage> History(string userId)
    {
        using var request = Authorized(HttpMethod.Get, "/api/matches/history", userId);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HistoryPage>())!;
    }

    private static HttpRequestMessage Machine(string matchId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{matchId}/finish");
        request.Headers.Add(TestAuthHandler.SubHeader, "8BWcE4T8HxhJrxU1CtgmNkjDOpkrN4Su@clients");
        request.Headers.Add(TestAuthHandler.GtyHeader, "client-credentials");
        request.Headers.Add(TestAuthHandler.AzpHeader, "8BWcE4T8HxhJrxU1CtgmNkjDOpkrN4Su");
        request.Headers.Add(TestAuthHandler.ScopeHeader, "matches.finish");
        return request;
    }

    private sealed record HistoryPage(HistoryItem[] Items);

    private sealed record HistoryItem(string MatchId, string GameType, bool IsHost);

    private async Task<MatchResponse> Create(string owner, string title, string gameType)
    {
        using var request = Authorized(HttpMethod.Post, "/api/matches", owner);
        request.Content = JsonContent.Create(new { title, gameType, maxPlayers = 4 });
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchResponse>())!;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string userId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthHandler.NameIdentifierHeader, userId);
        return request;
    }

    private async Task<MatchDocument> Load(string matchId)
    {
        var context = _factory.Services.GetRequiredService<MatchmakingMongoContext>();
        return await context.Matches.Find(match => match.Id == matchId).FirstAsync();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Cleanup:Enabled"] = "false",
                    ["Auth0:Domain"] = "test.auth0.invalid",
                    ["Auth0:Audience"] = "test-matchmaking-api",
                    ["MongoDb:ConnectionString"] = "mongodb://localhost:27017",
                    ["MongoDb:DatabaseName"] = DatabaseName
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
            });
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                var client = Services.GetService<IMongoClient>();
                if (client is not null)
                {
                    await client.DropDatabaseAsync(DatabaseName);
                }
            }
            catch (Exception)
            {
                // The test itself reports that MongoDB is unavailable.
            }

            await base.DisposeAsync();
        }
    }
}
