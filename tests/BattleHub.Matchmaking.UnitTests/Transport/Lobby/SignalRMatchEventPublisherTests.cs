using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Transport.Lobby;
using BattleHub.Matchmaking.Api.Transport.Matches;
using BattleHub.Matchmaking.Tests.Support;

namespace BattleHub.Matchmaking.Tests.Transport.Lobby;

public class SignalRMatchEventPublisherTests
{
    public static TheoryData<string> MatchGroupEvents => new()
    {
        MatchEventNames.PlayerJoined,
        MatchEventNames.PlayerLeft,
        MatchEventNames.MatchStarting,
        MatchEventNames.MatchStarted,
        MatchEventNames.MatchFinished,
        MatchEventNames.MatchDeleted
    };

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MatchCreated_SendsMatchResponseToLobby()
    {
        var (publisher, clients) = CreatePublisher();
        var match = CreateMatch();

        await publisher.PublishAsync(MatchEventNames.MatchCreated, match, CancellationToken.None);

        Assert.Equal([new[] { "lobby" }], clients.SelectedGroups);
        var send = Assert.Single(clients.Proxy.Sends);
        Assert.Equal("MatchCreated", send.Method);
        var payload = Assert.IsType<MatchResponse>(Assert.Single(send.Arguments));
        Assert.Equal("match-1", payload.Id);
        Assert.Equal(1, payload.CurrentPlayers);
        Assert.Equal("Waiting", payload.Status);
        Assert.Equal(DateTimeKind.Utc, payload.CreatedAt.Kind);
        Assert.Equal(new DateTime(2026, 10, 2, 18, 0, 0, DateTimeKind.Utc), payload.CreatedAt);
    }

    [Theory]
    [MemberData(nameof(MatchGroupEvents))]
    [Trait("Category", "Unit")]
    public async Task MatchEvent_SendsExactMethodToLobbyAndMatchGroup(string eventName)
    {
        var (publisher, clients) = CreatePublisher();

        await publisher.PublishAsync(eventName, CreateMatch(), CancellationToken.None);

        Assert.Equal([new[] { "lobby", "match:match-1" }], clients.SelectedGroups);
        var send = Assert.Single(clients.Proxy.Sends);
        Assert.Equal(eventName, send.Method);
        Assert.IsType<MatchResponse>(Assert.Single(send.Arguments));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UnknownEvent_IsRejectedWithoutSending()
    {
        var (publisher, clients) = CreatePublisher();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => publisher.PublishAsync("MatchUpdated", CreateMatch(), CancellationToken.None));

        Assert.Contains("not supported", exception.Message);
        Assert.Empty(clients.SelectedGroups);
        Assert.Empty(clients.Proxy.Sends);
    }

    private static (SignalRMatchEventPublisher Publisher, RecordingHubClients Clients) CreatePublisher()
    {
        var clients = new RecordingHubClients();
        var context = new TestHubContext<LobbyHub>(clients, new RecordingGroupManager());
        return (new SignalRMatchEventPublisher(context), clients);
    }

    private static Match CreateMatch()
    {
        var createdAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(-6));
        var match = new Match("match-1", "Friday", "Trivia", "owner-1", createdAt, 4);
        match.JoinParticipant("owner-1", createdAt);
        return match;
    }
}
