using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Domain.Matches;

public class MatchParticipantDisplayNameTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithDisplayName_PreservesIt()
    {
        var participant = new MatchParticipant("user-1", OccurredAt, OccurredAt, "Player One");

        Assert.Equal("Player One", participant.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "Unit")]
    public void Constructor_WithoutUsableDisplayName_FallsBackToUserId(string? displayName)
    {
        var participant = new MatchParticipant("user-1", OccurredAt, OccurredAt, displayName);

        Assert.Equal("user-1", participant.DisplayName);
    }
}
