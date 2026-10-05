using BattleHub.Matchmaking.Api.Application.Matches.Cleanup;
using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Application.Matches.Cleanup;

public class MatchCleanupEvaluatorTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = CreatedAt.AddMinutes(10);

    [Fact]
    [Trait("Category", "Unit")]
    public void IsInactiveWaiting_AtCutoff_ReturnsTrue() =>
        Assert.True(MatchCleanupEvaluator.IsInactiveWaiting(CreateMatch(MatchStatus.Waiting, Cutoff), Cutoff));

    [Fact]
    [Trait("Category", "Unit")]
    public void IsInactiveWaiting_AfterCutoff_ReturnsFalse() =>
        Assert.False(MatchCleanupEvaluator.IsInactiveWaiting(
            CreateMatch(MatchStatus.Waiting, Cutoff.AddTicks(1)), Cutoff));

    [Fact]
    [Trait("Category", "Unit")]
    public void IsInactiveWaiting_Started_ReturnsFalse() =>
        Assert.False(MatchCleanupEvaluator.IsInactiveWaiting(CreateMatch(MatchStatus.Started, CreatedAt), Cutoff));

    [Fact]
    [Trait("Category", "Unit")]
    public void HasNoValidHeartbeat_WithoutParticipants_ReturnsFalse() =>
        Assert.False(MatchCleanupEvaluator.HasNoValidHeartbeat(CreateMatch(), Cutoff));

    [Fact]
    [Trait("Category", "Unit")]
    public void HasNoValidHeartbeat_WithOneStaleParticipant_ReturnsTrue() =>
        Assert.True(MatchCleanupEvaluator.HasNoValidHeartbeat(
            CreateMatch(participants: [Participant("user-1", Cutoff.AddMinutes(-1))]), Cutoff));

    [Fact]
    [Trait("Category", "Unit")]
    public void HasNoValidHeartbeat_WithStaleAndValidParticipants_ReturnsFalse() =>
        Assert.False(MatchCleanupEvaluator.HasNoValidHeartbeat(
            CreateMatch(participants:
            [
                Participant("user-1", Cutoff.AddMinutes(-1)),
                Participant("user-2", Cutoff.AddTicks(1))
            ]), Cutoff));

    [Fact]
    [Trait("Category", "Unit")]
    public void HasNoValidHeartbeat_AtCutoff_ReturnsTrue() =>
        Assert.True(MatchCleanupEvaluator.HasNoValidHeartbeat(
            CreateMatch(participants: [Participant("user-1", Cutoff)]), Cutoff));

    [Fact]
    [Trait("Category", "Unit")]
    public void HasNoValidHeartbeat_AfterCutoff_ReturnsFalse() =>
        Assert.False(MatchCleanupEvaluator.HasNoValidHeartbeat(
            CreateMatch(participants: [Participant("user-1", Cutoff.AddTicks(1))]), Cutoff));

    [Theory]
    [InlineData(MatchStatus.Finished)]
    [InlineData(MatchStatus.Cancelled)]
    [Trait("Category", "Unit")]
    public void HasNoValidHeartbeat_TerminalMatch_ReturnsFalse(MatchStatus status) =>
        Assert.False(MatchCleanupEvaluator.HasNoValidHeartbeat(
            CreateMatch(status, participants: [Participant("user-1", Cutoff.AddMinutes(-1))]), Cutoff));

    [Theory]
    [InlineData(MatchStatus.Waiting, true)]
    [InlineData(MatchStatus.Starting, true)]
    [InlineData(MatchStatus.Started, true)]
    [InlineData(MatchStatus.Finished, false)]
    [InlineData(MatchStatus.Cancelled, false)]
    [Trait("Category", "Unit")]
    public void IsExpired_AtCutoff_OnlyAppliesToNonTerminalStatuses(MatchStatus status, bool expected) =>
        Assert.Equal(expected, MatchCleanupEvaluator.IsExpired(CreateMatch(status, CreatedAt), CreatedAt));

    [Theory]
    [InlineData(MatchStatus.Finished, true)]
    [InlineData(MatchStatus.Cancelled, true)]
    [InlineData(MatchStatus.Waiting, false)]
    [Trait("Category", "Unit")]
    public void IsTerminalRetentionExpired_AtCutoff_OnlyAppliesToTerminalStatuses(
        MatchStatus status,
        bool expected) =>
        Assert.Equal(expected, MatchCleanupEvaluator.IsTerminalRetentionExpired(
            CreateMatch(status, Cutoff), Cutoff));

    private static Match CreateMatch(
        MatchStatus status = MatchStatus.Waiting,
        DateTimeOffset? lastActivityAt = null,
        IReadOnlyCollection<MatchParticipant>? participants = null) =>
        Match.Rehydrate(
            "match-1",
            "Cleanup match",
            "Chess",
            "owner-1",
            CreatedAt,
            lastActivityAt ?? CreatedAt,
            4,
            status,
            participants ?? []);

    private static MatchParticipant Participant(string userId, DateTimeOffset heartbeatAt) =>
        new(userId, CreatedAt, heartbeatAt);
}
