using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Application.Matches;

public class MatchHistoryBoundaryTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, null)]
    [InlineData("TrIvIa", "trivia")]
    public void NormalizeGameType_AllowsMissingOrSupportedGame(string? input, string? expected) =>
        Assert.Equal(expected, MatchHistory.NormalizeGameType(input));

    [Theory]
    [InlineData("", "Game type is required.")]
    [InlineData(" ", "Game type is required.")]
    [InlineData("chess", "'chess' is not a supported game type.")]
    public void NormalizeGameType_RejectsInvalidInput(string input, string message) =>
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => MatchHistory.NormalizeGameType(input)).Message);

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 51, "pageSize")]
    public void EnsurePaging_RejectsValuesOutsideBounds(int page, int size, string parameter) =>
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => MatchHistory.EnsurePaging(page, size)).ParamName);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 50)]
    public void EnsurePaging_AcceptsInclusiveBounds(int page, int size) => MatchHistory.EnsurePaging(page, size);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RecordedAt_UsesFirstAvailableTimestamp(int available)
    {
        var started = Created.AddMinutes(1);
        var cancelled = Created.AddMinutes(2);
        var finished = Created.AddMinutes(3);
        var match = Match.Rehydrate("id", "title", "trivia", "host", Created, Created, 2,
            MatchStatus.Finished, [], available >= 1 ? started : null,
            available >= 3 ? finished : null, available >= 2 ? cancelled : null);

        Assert.Equal(available switch { 3 => finished, 2 => cancelled, 1 => started, _ => Created }, MatchHistory.RecordedAt(match));
    }
}
