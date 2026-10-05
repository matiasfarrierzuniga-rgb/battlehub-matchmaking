using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Domain.Matches;

public class MatchMembershipTests
{
    private const string OwnerId = "owner-1";
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAt = CreatedAt.AddMinutes(5);

    [Fact]
    [Trait("Category", "Unit")]
    public void JoinParticipant_InWaiting_AddsParticipant()
    {
        var match = CreateMatch();

        var joined = match.JoinParticipant("user-1", OccurredAt);

        Assert.True(joined);
        Assert.Contains(match.Participants, participant => participant.UserId == "user-1");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void JoinParticipant_WhenSuccessful_UpdatesCountAndTimestamps()
    {
        var match = CreateMatch();

        match.JoinParticipant("user-1", OccurredAt);

        var participant = Assert.Single(match.Participants);
        Assert.Equal(1, match.CurrentPlayers);
        Assert.Equal(OccurredAt, participant.JoinedAt);
        Assert.Equal(OccurredAt, participant.LastHeartbeatAt);
        Assert.Equal(OccurredAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void JoinParticipant_WithEarlierTimestamp_DoesNotMoveLastActivityBackward()
    {
        var match = CreateMatch();
        match.JoinParticipant("user-1", OccurredAt);

        match.JoinParticipant("user-2", CreatedAt.AddMinutes(2));

        Assert.Equal(OccurredAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void JoinParticipant_WhenDuplicate_IsIdempotent()
    {
        var match = CreateMatch();
        match.JoinParticipant("user-1", CreatedAt.AddMinutes(1));
        var lastActivityAt = match.LastActivityAt;

        var joined = match.JoinParticipant("user-1", OccurredAt);

        Assert.False(joined);
        Assert.Equal(1, match.CurrentPlayers);
        Assert.Equal(lastActivityAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void JoinParticipant_WhenFull_IsRejected()
    {
        var match = CreateMatch(maxPlayers: 1);
        match.JoinParticipant("user-1", CreatedAt.AddMinutes(1));

        Assert.Throws<MatchFullException>(() => match.JoinParticipant("user-2", OccurredAt));
        Assert.Equal(1, match.CurrentPlayers);
    }

    [Theory]
    [InlineData(MatchStatus.Starting)]
    [InlineData(MatchStatus.Started)]
    [InlineData(MatchStatus.Finished)]
    [InlineData(MatchStatus.Cancelled)]
    [Trait("Category", "Unit")]
    public void JoinParticipant_OutsideWaiting_IsRejected(MatchStatus status)
    {
        var match = CreateInStatus(status);

        var error = Assert.Throws<MatchMembershipChangeNotAllowedException>(
            () => match.JoinParticipant("user-1", OccurredAt));
        Assert.Equal(status, error.CurrentStatus);
        Assert.Empty(match.Participants);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LeaveParticipant_WhenPresent_RemovesParticipantAndUpdatesActivity()
    {
        var match = CreateMatch();
        match.JoinParticipant("user-1", CreatedAt.AddMinutes(1));

        var left = match.LeaveParticipant("user-1", OccurredAt);

        Assert.True(left);
        Assert.Empty(match.Participants);
        Assert.Equal(0, match.CurrentPlayers);
        Assert.Equal(OccurredAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LeaveParticipant_WithEarlierTimestamp_DoesNotMoveLastActivityBackward()
    {
        var match = CreateMatch();
        match.JoinParticipant("user-1", OccurredAt);

        match.LeaveParticipant("user-1", CreatedAt.AddMinutes(2));

        Assert.Equal(OccurredAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LeaveParticipant_WhenAbsent_IsIdempotent()
    {
        var match = CreateMatch();
        var lastActivityAt = match.LastActivityAt;

        var left = match.LeaveParticipant("user-1", OccurredAt);

        Assert.False(left);
        Assert.Equal(lastActivityAt, match.LastActivityAt);
    }

    [Theory]
    [InlineData(MatchStatus.Starting)]
    [InlineData(MatchStatus.Started)]
    [InlineData(MatchStatus.Finished)]
    [InlineData(MatchStatus.Cancelled)]
    [Trait("Category", "Unit")]
    public void LeaveParticipant_OutsideWaiting_IsRejected(MatchStatus status)
    {
        var match = CreateInStatus(status);

        var error = Assert.Throws<MatchMembershipChangeNotAllowedException>(
            () => match.LeaveParticipant("user-1", OccurredAt));
        Assert.Equal(status, error.CurrentStatus);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_DoesNotAutomaticallyJoinOwner()
    {
        var match = CreateMatch();

        Assert.Empty(match.Participants);
        Assert.Equal(0, match.CurrentPlayers);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Owner_CanLeaveParticipantsWithoutChangingCreatedBy()
    {
        var match = CreateMatch();
        match.JoinParticipant(OwnerId, CreatedAt.AddMinutes(1));

        var left = match.LeaveParticipant(OwnerId, OccurredAt);

        Assert.True(left);
        Assert.DoesNotContain(match.Participants, participant => participant.UserId == OwnerId);
        Assert.Equal(OwnerId, match.CreatedBy);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [Trait("Category", "Unit")]
    public void JoinParticipant_WithEmptyUserId_IsRejected(string userId)
    {
        var match = CreateMatch();

        Assert.Throws<ArgumentException>(() => match.JoinParticipant(userId, OccurredAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [Trait("Category", "Unit")]
    public void LeaveParticipant_WithEmptyUserId_IsRejected(string userId)
    {
        var match = CreateMatch();

        Assert.Throws<ArgumentException>(() => match.LeaveParticipant(userId, OccurredAt));
    }

    private static Match CreateMatch(int maxPlayers = 2) =>
        new("match-1", "Friday match", "Chess", OwnerId, CreatedAt, maxPlayers);

    private static Match CreateInStatus(MatchStatus status)
    {
        var match = CreateMatch();

        switch (status)
        {
            case MatchStatus.Starting:
                match.RequestStart(OwnerId, CreatedAt.AddMinutes(1));
                break;
            case MatchStatus.Started:
                match.RequestStart(OwnerId, CreatedAt.AddMinutes(1));
                match.CompleteStart(CreatedAt.AddMinutes(2));
                break;
            case MatchStatus.Finished:
                match.RequestStart(OwnerId, CreatedAt.AddMinutes(1));
                match.CompleteStart(CreatedAt.AddMinutes(2));
                match.Finish(CreatedAt.AddMinutes(3));
                break;
            case MatchStatus.Cancelled:
                match.Cancel(OwnerId, CreatedAt.AddMinutes(1));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return match;
    }
}
