using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Domain.Matches;

public class MatchHeartbeatTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset JoinedAt = CreatedAt.AddMinutes(1);

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordHeartbeat_UpdatesParticipantAndMatchWithoutChangingJoinedAt()
    {
        var match = CreateMatch();
        var heartbeatAt = CreatedAt.AddMinutes(5);

        var changed = match.RecordHeartbeat("user-1", heartbeatAt);

        var participant = Assert.Single(match.Participants);
        Assert.True(changed);
        Assert.Equal(JoinedAt, participant.JoinedAt);
        Assert.Equal(heartbeatAt, participant.LastHeartbeatAt);
        Assert.Equal(heartbeatAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordHeartbeat_WithEarlierTimestamp_IsValidAndDoesNotMoveTimestampsBackward()
    {
        var participantHeartbeat = CreatedAt.AddMinutes(5);
        var activity = CreatedAt.AddMinutes(6);
        var match = Rehydrate(participantHeartbeat, activity, MatchStatus.Waiting);

        var changed = match.RecordHeartbeat("user-1", CreatedAt.AddMinutes(4));

        Assert.False(changed);
        Assert.Equal(participantHeartbeat, Assert.Single(match.Participants).LastHeartbeatAt);
        Assert.Equal(activity, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordHeartbeat_CanUpdateOnlyParticipantTimestamp()
    {
        var activity = CreatedAt.AddMinutes(10);
        var heartbeatAt = CreatedAt.AddMinutes(5);
        var match = Rehydrate(JoinedAt, activity, MatchStatus.Waiting);

        var changed = match.RecordHeartbeat("user-1", heartbeatAt);

        Assert.True(changed);
        Assert.Equal(heartbeatAt, Assert.Single(match.Participants).LastHeartbeatAt);
        Assert.Equal(activity, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordHeartbeat_WhenParticipantIsMissing_ThrowsWithoutChangingTimestamps()
    {
        var match = CreateMatch();
        var participant = Assert.Single(match.Participants);
        var heartbeat = participant.LastHeartbeatAt;
        var activity = match.LastActivityAt;

        var error = Assert.Throws<MatchParticipantNotFoundException>(
            () => match.RecordHeartbeat("other-user", CreatedAt.AddMinutes(5)));

        Assert.Equal("other-user", error.UserId);
        Assert.Equal(heartbeat, participant.LastHeartbeatAt);
        Assert.Equal(activity, match.LastActivityAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [Trait("Category", "Unit")]
    public void RecordHeartbeat_WithEmptyUserId_Throws(string userId)
    {
        var match = CreateMatch();

        Assert.Throws<ArgumentException>(() => match.RecordHeartbeat(userId, CreatedAt));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordHeartbeat_WorksWhenMatchIsStarted()
    {
        var match = Rehydrate(JoinedAt, JoinedAt, MatchStatus.Started);
        var heartbeatAt = CreatedAt.AddMinutes(5);

        var changed = match.RecordHeartbeat("user-1", heartbeatAt);

        Assert.True(changed);
        Assert.Equal(heartbeatAt, Assert.Single(match.Participants).LastHeartbeatAt);
        Assert.Equal(heartbeatAt, match.LastActivityAt);
        Assert.Equal(MatchStatus.Started, match.Status);
    }

    private static Match CreateMatch()
    {
        var match = new Match("match-1", "Friday match", "Chess", "owner-1", CreatedAt, 4);
        match.JoinParticipant("user-1", JoinedAt);
        return match;
    }

    private static Match Rehydrate(
        DateTimeOffset lastHeartbeatAt,
        DateTimeOffset lastActivityAt,
        MatchStatus status) =>
        Match.Rehydrate(
            "match-1", "Friday match", "Chess", "owner-1", CreatedAt, lastActivityAt, 4, status,
            [new MatchParticipant("user-1", JoinedAt, lastHeartbeatAt)]);
}
