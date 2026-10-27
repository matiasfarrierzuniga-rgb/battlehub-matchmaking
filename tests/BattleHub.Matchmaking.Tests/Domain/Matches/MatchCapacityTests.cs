using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Domain.Matches;

public class MatchCapacityTests
{
    private const string OwnerId = "owner-1";
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("typing", 4)]
    [InlineData("Typing", 8)]
    [InlineData("trivia", 4)]
    [InlineData("Trivia", 6)]
    [InlineData("Chess", 4)]
    [Trait("Category", "Unit")]
    public void Constructor_KeepsRequestedCapacityOutsideMemory(string gameType, int requestedMaxPlayers)
    {
        var match = new Match("match-1", "Friday match", gameType, OwnerId, CreatedAt, requestedMaxPlayers);

        Assert.Equal(requestedMaxPlayers, match.MaxPlayers);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_MemoryWithTwoPlayers_IsAllowed()
    {
        var match = new Match("match-1", "Friday match", "memory", OwnerId, CreatedAt, 2);

        Assert.Equal(2, match.MaxPlayers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(10)]
    [Trait("Category", "Unit")]
    public void Constructor_MemoryWithoutExactlyTwoPlayers_IsRejected(int requestedMaxPlayers)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new Match("match-1", "Friday match", "memory", OwnerId, CreatedAt, requestedMaxPlayers));

        Assert.Equal("Memory requires exactly 2 players.", error.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Memory_AcceptsTwoPlayersAndRejectsTheThird()
    {
        var match = new Match("match-1", "Friday match", "memory", OwnerId, CreatedAt, 2);

        Assert.True(match.JoinParticipant("user-1", CreatedAt.AddMinutes(1)));
        Assert.True(match.JoinParticipant("user-2", CreatedAt.AddMinutes(2)));
        var error = Assert.Throws<MatchFullException>(
            () => match.JoinParticipant("user-3", CreatedAt.AddMinutes(3)));

        Assert.Equal(2, match.CurrentPlayers);
        Assert.Equal(2, match.MaxPlayers);
        Assert.Equal(2, error.MaxPlayers);
        Assert.Contains("is full", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(match.Participants, participant => participant.UserId == "user-3");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rehydrate_MemoryWithoutExactlyTwoPlayers_IsRejected()
    {
        var participants = new[]
        {
            new MatchParticipant("user-1", CreatedAt, CreatedAt),
            new MatchParticipant("user-2", CreatedAt, CreatedAt)
        };

        var error = Assert.Throws<ArgumentException>(() => Match.Rehydrate(
            "match-1",
            "Friday match",
            "memory",
            OwnerId,
            CreatedAt,
            CreatedAt,
            10,
            MatchStatus.Waiting,
            participants));

        Assert.Equal("Memory requires exactly 2 players.", error.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rehydrate_MemoryWithMoreParticipantsThanCap_IsRejected()
    {
        var participants = new[]
        {
            new MatchParticipant("user-1", CreatedAt, CreatedAt),
            new MatchParticipant("user-2", CreatedAt, CreatedAt),
            new MatchParticipant("user-3", CreatedAt, CreatedAt)
        };

        Assert.Throws<ArgumentException>(() => Match.Rehydrate(
            "match-1",
            "Friday match",
            "memory",
            OwnerId,
            CreatedAt,
            CreatedAt,
            10,
            MatchStatus.Waiting,
            participants));
    }
}
