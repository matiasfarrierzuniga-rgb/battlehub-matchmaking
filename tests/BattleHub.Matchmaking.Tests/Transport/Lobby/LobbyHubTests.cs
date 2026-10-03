using BattleHub.Matchmaking.Api.Transport.Lobby;
using BattleHub.Matchmaking.Tests.Support;

namespace BattleHub.Matchmaking.Tests.Transport.Lobby;

public class LobbyHubTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task JoinLobby_AddsConnectionToLobbyGroup()
    {
        var (hub, groups) = CreateHub();

        await hub.JoinLobby();

        Assert.Equal([("connection-1", "lobby")], groups.Added);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task JoinMatch_AddsConnectionToMatchGroup()
    {
        var (hub, groups) = CreateHub();

        await hub.JoinMatch("match-1");

        Assert.Equal([("connection-1", "match:match-1")], groups.Added);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task LeaveMatch_RemovesConnectionFromMatchGroup()
    {
        var (hub, groups) = CreateHub();

        await hub.LeaveMatch("match-1");

        Assert.Equal([("connection-1", "match:match-1")], groups.Removed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task JoinMatch_WithoutId_Throws()
    {
        var (hub, _) = CreateHub();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => hub.JoinMatch(" "));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task LeaveMatch_WithoutId_Throws()
    {
        var (hub, _) = CreateHub();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => hub.LeaveMatch(string.Empty));
    }

    private static (LobbyHub Hub, RecordingGroupManager Groups) CreateHub()
    {
        var groups = new RecordingGroupManager();
        var hub = new LobbyHub
        {
            Context = new TestHubCallerContext("connection-1"),
            Groups = groups
        };
        return (hub, groups);
    }
}
