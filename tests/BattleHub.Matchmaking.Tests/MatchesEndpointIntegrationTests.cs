using System.Net;
using System.Net.Http.Json;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Application.Matches.Cleanup;
using BattleHub.Matchmaking.Api.Configuration;
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
using Microsoft.Extensions.Options;

namespace BattleHub.Matchmaking.Tests;

public class MatchesEndpointIntegrationTests : IClassFixture<MatchesEndpointIntegrationTests.Factory>
{
    private readonly HttpClient _client;
    private readonly Factory _factory;

    public MatchesEndpointIntegrationTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void MatchCleanupService_IsRegistered() =>
        Assert.NotNull(_factory.Services.GetService<MatchCleanupService>());

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
        Assert.Contains("\"gameType\":\"trivia\"", json);
        Assert.Contains("\"participants\":[{\"userId\":\"owner-1\",\"displayName\":\"owner-1\"}]", json);
        Assert.DoesNotContain("joinedAt", json);
        Assert.DoesNotContain("lastHeartbeatAt", json);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cors_ConfiguredShellOrigin_IsAllowedWithCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/matches");
        request.Headers.Add("Origin", "http://localhost:4000");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await _client.SendAsync(request);

        Assert.Equal("http://localhost:4000", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cors_UnconfiguredOrigin_IsNotAllowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/matches");
        request.Headers.Add("Origin", "http://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
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
    public async Task Finish_WithoutPrincipal_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsync("/api/matches/typing-started/finish", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Finish_WithNormalUser_ReturnsForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matches/typing-started/finish");
        request.Headers.Add(TestAuthHandler.SubHeader, "user-1");

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Finish_WithMachineMissingPermission_ReturnsForbidden()
    {
        using var response = await SendAsMachine("/api/matches/typing-started/finish", "matches.create");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Finish_WithAuthorizedMachine_IsIdempotent()
    {
        using var first = await SendAsMachine("/api/matches/typing-started/finish", "matches.finish");
        using var second = await SendAsMachine("/api/matches/typing-started/finish", "matches.finish");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Finish_WithMachineForDifferentGame_ReturnsForbidden()
    {
        using var response = await SendAsMachine("/api/matches/trivia-started/finish", "matches.finish");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ValidFinishMachine_CannotCreateUserMatch()
    {
        using var request = MachineRequest(HttpMethod.Post, "/api/matches", "matches.finish");
        request.Content = JsonContent.Create(new { title = "Forbidden", gameType = "typing", maxPlayers = 4 });

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    [Fact]
    [Trait("Category", "Integration")]
    public void GameServiceClients_ResolveTypingTriviaAndMemory()
    {
        var clients = _factory.Services.GetRequiredService<IOptions<GameServiceOptions>>().Value.Clients;

        Assert.Equal("typing", clients["8BWcE4T8HxhJrxU1CtgmNkjDOpkrN4Su"]);
        Assert.Equal("trivia", clients["xYhNYG9lOGLA6KvEM0Y4cccH2f7JDD5x"]);
        Assert.Equal("memory", clients["lfcBlgOCs9N0w6F6AWl4FqOs4xF9AQiN"]);
        Assert.False(clients.ContainsKey("unknown-client"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(10)]
    [Trait("Category", "Integration")]
    public async Task PostMemoryMatch_NeverStoresCapacityAboveTwo(int requestedMaxPlayers)
    {
        var created = await CreateAuthenticatedMatch(
            $"memory-owner-{requestedMaxPlayers}",
            "Memory room",
            "memory",
            requestedMaxPlayers);

        Assert.Equal("memory", created.GameType);
        Assert.Equal(2, created.MaxPlayers);
        Assert.Equal(1, created.CurrentPlayers);
    }

    [Theory]
    [InlineData("typing", 4)]
    [InlineData("Typing", 8)]
    [InlineData("trivia", 4)]
    [InlineData("Trivia", 6)]
    [Trait("Category", "Integration")]
    public async Task PostMatch_TypingAndTrivia_KeepRequestedCapacity(string gameType, int maxPlayers)
    {
        var created = await CreateAuthenticatedMatch(
            $"capacity-{gameType}-{maxPlayers}",
            "Capacity room",
            gameType,
            maxPlayers);

        Assert.Equal(gameType.ToLowerInvariant(), created.GameType);
        Assert.Equal(maxPlayers, created.MaxPlayers);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MemoryMatch_AcceptsTwoPlayersAndRejectsTheThird()
    {
        var created = await CreateAuthenticatedMatch("memory-owner", "Memory room", "Memory", 10);

        Assert.Equal("memory", created.GameType);
        Assert.Equal(2, created.MaxPlayers);
        Assert.Equal(1, created.CurrentPlayers);
        Assert.Equal("memory-owner", Assert.Single(created.Participants).UserId);

        using var joinSecond = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{created.Id}/join");
        joinSecond.Headers.Add(TestAuthHandler.SubHeader, "memory-player-2");
        using var secondResponse = await _client.SendAsync(joinSecond);
        var second = await secondResponse.Content.ReadFromJsonAsync<MatchResponse>();

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(2, second!.CurrentPlayers);
        Assert.Equal(2, second.MaxPlayers);
        Assert.Contains(second.Participants, participant => participant.UserId == "memory-player-2");

        using var joinThird = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{created.Id}/join");
        joinThird.Headers.Add(TestAuthHandler.SubHeader, "memory-player-3");
        using var thirdResponse = await _client.SendAsync(joinThird);
        var thirdBody = await thirdResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, thirdResponse.StatusCode);
        Assert.Equal("application/problem+json", thirdResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("is full", thirdBody, StringComparison.OrdinalIgnoreCase);

        using var getResponse = await _client.GetAsync($"/api/matches/{created.Id}");
        var stored = await getResponse.Content.ReadFromJsonAsync<MatchResponse>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(2, stored!.CurrentPlayers);
        Assert.Equal(2, stored.MaxPlayers);
        Assert.Equal(2, stored.Participants.Length);
        Assert.DoesNotContain(stored.Participants, participant => participant.UserId == "memory-player-3");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Finish_WithConfiguredMemoryClient_SucceedsOnlyForMemory()
    {
        var created = await CreateAuthenticatedMatch("memory-finish-owner", "Finish memory", "memory", 2);
        using var start = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{created.Id}/start");
        start.Headers.Add(TestAuthHandler.NameIdentifierHeader, "memory-finish-owner");
        using var startResponse = await _client.SendAsync(start);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        using var memoryFinish = await _client.SendAsync(MachineRequest(
            HttpMethod.Post,
            $"/api/matches/{created.Id}/finish",
            "matches.finish",
            "lfcBlgOCs9N0w6F6AWl4FqOs4xF9AQiN"));
        using var typingOnMemory = await _client.SendAsync(MachineRequest(
            HttpMethod.Post,
            $"/api/matches/{created.Id}/finish",
            "matches.finish",
            "8BWcE4T8HxhJrxU1CtgmNkjDOpkrN4Su"));

        Assert.Equal(HttpStatusCode.NoContent, memoryFinish.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, typingOnMemory.StatusCode);
    }

    [Theory]
    [InlineData("typing-started", "8BWcE4T8HxhJrxU1CtgmNkjDOpkrN4Su")]
    [InlineData("trivia-started", "xYhNYG9lOGLA6KvEM0Y4cccH2f7JDD5x")]
    [Trait("Category", "Integration")]
    public async Task Finish_WithConfiguredGameClient_Succeeds(string matchId, string clientId)
    {
        using var response = await _client.SendAsync(MachineRequest(
            HttpMethod.Post,
            $"/api/matches/{matchId}/finish",
            "matches.finish",
            clientId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Finish_WithUnknownClient_ReturnsForbidden()
    {
        using var response = await _client.SendAsync(MachineRequest(
            HttpMethod.Post,
            "/api/matches/typing-started/finish",
            "matches.finish",
            "unknown-client"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<MatchResponse> CreateAuthenticatedMatch(
        string owner,
        string title,
        string gameType = "Trivia",
        int maxPlayers = 4)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matches")
        {
            Content = JsonContent.Create(new { title, gameType, maxPlayers })
        };
        request.Headers.Add(TestAuthHandler.NameIdentifierHeader, owner);

        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchResponse>())!;
    }

    private async Task<HttpResponseMessage> SendAsMachine(string path, string scope) =>
        await _client.SendAsync(MachineRequest(HttpMethod.Post, path, scope));

    private static HttpRequestMessage MachineRequest(
        HttpMethod method,
        string path,
        string scope,
        string clientId = "typing-client")
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthHandler.SubHeader, $"{clientId}@clients");
        request.Headers.Add(TestAuthHandler.GtyHeader, "client-credentials");
        request.Headers.Add(TestAuthHandler.AzpHeader, clientId);
        request.Headers.Add(TestAuthHandler.ScopeHeader, scope);
        return request;
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
                    ["GameServices:Clients:typing-client"] = "typing"
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
                var waitingMatch = new Match("match-1", "Friday", "Trivia", "owner-1", createdAt, 4);
                waitingMatch.JoinParticipant("owner-1", createdAt);
                store.Add(waitingMatch);
                store.Add(StartedMatch("typing-started", "typing", createdAt));
                store.Add(StartedMatch("trivia-started", "trivia", createdAt));
                services.AddSingleton<IMatchStore>(store);
            });
        }

        private static Match StartedMatch(string id, string gameType, DateTimeOffset createdAt)
        {
            var match = new Match(id, "Started", gameType, "owner-1", createdAt, 4);
            match.RequestStart("owner-1", createdAt.AddMinutes(1));
            match.CompleteStart(createdAt.AddMinutes(2));
            return match;
        }
    }
}
