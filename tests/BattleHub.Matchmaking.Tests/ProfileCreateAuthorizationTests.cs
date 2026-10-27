using System.Net;
using System.Net.Http.Json;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Infrastructure.Profiles;
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

public class ProfileCreateAuthorizationTests : IClassFixture<ProfileCreateAuthorizationTests.Factory>
{
    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ProfileCreateAuthorizationTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WithoutIdentity_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/matches",
            new { title = "No token", gameType = "memory", maxPlayers = 2 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await ListedTitles());
    }

    [Theory]
    [InlineData("typing", "games.typing.play")]
    [InlineData("trivia", "games.trivia.play")]
    [InlineData("memory", "games.memory.play")]
    [Trait("Category", "Integration")]
    public async Task Create_WithBothPermissions_IsAllowed(string gameType, string playPermission)
    {
        _factory.Profiles.PermissionsError = null;
        _factory.Profiles.Permissions = ["matches.create", playPermission];

        using var response = await Post(gameType, $"{gameType} room");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains($"{gameType} room", await ListedTitles());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WithoutCreatePermission_IsForbiddenAndNotStored()
    {
        _factory.Profiles.PermissionsError = null;
        _factory.Profiles.Permissions = ["games.memory.play"];

        using var response = await Post("memory", "Missing create");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Missing create", await ListedTitles());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WithoutGamePermission_IsForbiddenAndNotStored()
    {
        _factory.Profiles.PermissionsError = null;
        _factory.Profiles.Permissions = ["matches.create", "games.typing.play"];

        using var response = await Post("memory", "Typing cannot open memory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Typing cannot open memory", await ListedTitles());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("chess")]
    [InlineData("Chess")]
    [Trait("Category", "Integration")]
    public async Task Create_WithUnsupportedGameType_IsBadRequest(string gameType)
    {
        _factory.Profiles.PermissionsError = new ProfileUnavailableException();

        using var response = await Post(gameType, $"Rejected {gameType}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain($"Rejected {gameType}", await ListedTitles());
        _factory.Profiles.PermissionsError = null;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WithNullGameType_IsBadRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matches")
        {
            Content = JsonContent.Create(new { title = "Null game", gameType = (string?)null, maxPlayers = 2 })
        };
        request.Headers.Add(TestAuthHandler.NameIdentifierHeader, "player-1");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Null game", await ListedTitles());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WithUnknownGameType_IsBadRequest()
    {
        _factory.Profiles.PermissionsError = new ProfileUnavailableException();

        using var response = await Post("chess", "Unknown game");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Unknown game", await ListedTitles());
        _factory.Profiles.PermissionsError = null;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WhenProfileIsUnavailable_ReturnsServiceUnavailableAndDoesNotStore()
    {
        _factory.Profiles.PermissionsError = new ProfileUnavailableException();

        using var response = await Post("memory", "Profile down");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Profile Service no está disponible.", body);
        Assert.DoesNotContain("Profile down", await ListedTitles());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_WithGamePermission_IsAllowed()
    {
        _factory.Profiles.PermissionsError = null;
        _factory.Profiles.Permissions = ["matches.create", "games.memory.play"];
        var created = await Create("memory", "Joinable memory");

        _factory.Profiles.Permissions = ["games.memory.play"];
        using var response = await Join(created.Id, "player-2");
        var match = await response.Content.ReadFromJsonAsync<MatchBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, match!.CurrentPlayers);
    }

    [Theory]
    [InlineData("typing", "games.trivia.play")]
    [InlineData("trivia", "games.typing.play")]
    [InlineData("memory", "games.typing.play")]
    [Trait("Category", "Integration")]
    public async Task Join_WithoutGamePermission_IsForbiddenAndDoesNotAddParticipant(
        string gameType,
        string otherPermission)
    {
        var maxPlayers = gameType == "memory" ? 2 : 4;
        _factory.Profiles.PermissionsError = null;
        _factory.Profiles.Permissions = ["matches.create", $"games.{gameType}.play"];
        var created = await Create(gameType, $"Join {gameType}", maxPlayers);

        _factory.Profiles.Permissions = [otherPermission];
        using var response = await Join(created.Id, "outsider");
        var stored = await Get(created.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, stored.CurrentPlayers);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_WhenProfileIsUnavailable_ReturnsServiceUnavailableAndDoesNotAddParticipant()
    {
        _factory.Profiles.PermissionsError = null;
        _factory.Profiles.Permissions = ["matches.create", "games.memory.play"];
        var created = await Create("memory", "Profile down join");

        _factory.Profiles.PermissionsError = new ProfileUnavailableException();
        using var response = await Join(created.Id, "player-2");
        var body = await response.Content.ReadAsStringAsync();
        _factory.Profiles.PermissionsError = null;
        var stored = await Get(created.Id);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Profile Service no está disponible.", body);
        Assert.Equal(1, stored.CurrentPlayers);
    }

    private async Task<MatchBody> Create(string gameType, string title, int maxPlayers = 2)
    {
        using var response = await Post(gameType, title, maxPlayers);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchBody>())!;
    }

    private async Task<HttpResponseMessage> Join(string matchId, string userId)
    {
        using var request = Authenticated(HttpMethod.Post, $"/api/matches/{matchId}/join", userId);
        request.Headers.Add("Authorization", "Bearer player-token");
        return await _client.SendAsync(request);
    }

    private async Task<MatchBody> Get(string matchId)
    {
        using var request = Authenticated(HttpMethod.Get, $"/api/matches/{matchId}");
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchBody>())!;
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, string path, string userId = "reader-1")
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthHandler.NameIdentifierHeader, userId);
        return request;
    }

    private async Task<HttpResponseMessage> Post(string gameType, string title, int maxPlayers = 2)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matches")
        {
            Content = JsonContent.Create(new { title, gameType, maxPlayers })
        };
        request.Headers.Add(TestAuthHandler.NameIdentifierHeader, "player-1");
        request.Headers.Add("Authorization", "Bearer player-token");
        return await _client.SendAsync(request);
    }

    private sealed record MatchBody(string Id, int CurrentPlayers);

    private async Task<IReadOnlyList<string>> ListedTitles()
    {
        using var request = Authenticated(HttpMethod.Get, "/api/matches");
        var matches = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<MatchListItem[]>();
        return matches!.Select(match => match.Title).ToArray();
    }

    private sealed record MatchListItem(string Title);

    public sealed class Factory : WebApplicationFactory<Program>
    {
        internal GrantingProfileDirectory Profiles { get; } = new();

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
                services.AddSingleton<IProfileDirectory>(Profiles);
                services.RemoveAll<IMatchHistoryStore>();
                services.AddSingleton<IMatchHistoryStore>(new MemoryMatchHistoryStore());
                services.RemoveAll<IMatchStore>();
                services.AddSingleton<IMatchStore>(Store);
            });
        }
    }
}
