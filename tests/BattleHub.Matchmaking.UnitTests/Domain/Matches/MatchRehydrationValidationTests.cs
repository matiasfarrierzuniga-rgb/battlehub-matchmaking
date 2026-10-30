using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Domain.Matches;

public class MatchRehydrationValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_RejectsZeroCapacity() =>
        Assert.Equal("maxPlayers", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Match("id", "title", "trivia", "host", Now, 0)).ParamName);

    [Fact]
    public void Rehydrate_RejectsUnknownStatus() =>
        Assert.Equal("status", Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrate((MatchStatus)999, [])).ParamName);

    [Fact]
    public void Rehydrate_RejectsNullParticipant()
    {
        var exception = Assert.Throws<ArgumentException>(() => Rehydrate(MatchStatus.Waiting, [null!]));
        Assert.Equal("participants", exception.ParamName);
    }

    [Fact]
    public void Rehydrate_RejectsDuplicateUsers()
    {
        var participant = new MatchParticipant("user", Now, Now, null);
        var exception = Assert.Throws<ArgumentException>(() => Rehydrate(MatchStatus.Waiting, [participant, participant]));
        Assert.Equal("participants", exception.ParamName);
        Assert.Contains("unique", exception.Message);
    }

    private static Match Rehydrate(MatchStatus status, IEnumerable<MatchParticipant> participants) =>
        Match.Rehydrate("id", "title", "trivia", "host", Now, Now, 2, status, participants);
}
