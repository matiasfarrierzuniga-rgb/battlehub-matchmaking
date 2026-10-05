using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Domain.Matches;

public class MatchCapacityTests
{
    private const string OwnerId = "owner-1";
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("memory", 2, 2)]
    [InlineData("memory", 3, 2)]
    [InlineData("Memory", 4, 2)]
    [InlineData("MEMORY", 10, 2)]
    [InlineData("typing", 4, 4)]
    [InlineData("Typing", 8, 8)]
    [InlineData("trivia", 4, 4)]
    [InlineData("Trivia", 6, 6)]
    [InlineData("Chess", 4, 4)]
    [Trait("Category", "Unit")]
    public void Constructor_CapsOnlyMemory(string gameType, int requestedMaxPlayers, int expectedMaxPlayers)
    {
        var match = new Match("match-1", "Friday match", gameType, OwnerId, CreatedAt, requestedMaxPlayers);

        Assert.Equal(expectedMaxPlayers, match.MaxPlayers);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Memory_AcceptsTwoPlayersAndRejectsTheThird()
    {
        var match = new Match("match-1", "Friday match", "memory", OwnerId, CreatedAt, 10);

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
    public void Rehydrate_MemoryAboveTwo_StoresTheCappedCapacity()
    {
        var participants = new[]
        {
            new MatchParticipant("user-1", CreatedAt, CreatedAt),
            new MatchParticipant("user-2", CreatedAt, CreatedAt)
        };

        var match = Match.Rehydrate(
            "match-1",
            "Friday match",
            "memory",
            OwnerId,
            CreatedAt,
            CreatedAt,
            10,
            MatchStatus.Waiting,
            participants);

        Assert.Equal(2, match.MaxPlayers);
        Assert.Equal(2, match.CurrentPlayers);
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
