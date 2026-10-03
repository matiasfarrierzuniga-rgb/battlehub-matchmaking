using BattleHub.Matchmaking.Api.Transport.Lobby;

namespace BattleHub.Matchmaking.Tests.Transport.Lobby;

public class LobbyGroupsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ForMatch_WithId_ReturnsNamespacedGroup()
    {
        Assert.Equal("match:match-1", LobbyGroups.ForMatch("match-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "Unit")]
    public void ForMatch_WithoutId_Throws(string? matchId)
    {
        Assert.ThrowsAny<ArgumentException>(() => LobbyGroups.ForMatch(matchId!));
    }
}
