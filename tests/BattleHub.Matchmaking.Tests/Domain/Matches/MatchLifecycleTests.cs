using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Domain.Matches;

public class MatchLifecycleTests
{
    private const string OwnerId = "owner-1";
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TransitionAt = CreatedAt.AddMinutes(5);

    [Fact]
    [Trait("Category", "Unit")]
    public void NewMatch_StartsInWaitingWithoutParticipants()
    {
        var match = CreateMatch();

        Assert.Equal(MatchStatus.Waiting, match.Status);
        Assert.Equal(0, match.CurrentPlayers);
        Assert.Empty(match.Participants);
        Assert.Equal(CreatedAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequestStart_ByOwner_TransitionsWaitingToStarting()
    {
        var match = CreateMatch();

        match.RequestStart(OwnerId, TransitionAt);

        Assert.Equal(MatchStatus.Starting, match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequestStart_ByNonOwner_IsRejected()
    {
        var match = CreateMatch();

        Assert.Throws<UnauthorizedAccessException>(() => match.RequestStart("other-user", TransitionAt));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CompleteStart_FromStarting_TransitionsToStarted()
    {
        var match = CreateStartingMatch();

        match.CompleteStart(TransitionAt);

        Assert.Equal(MatchStatus.Started, match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CompleteStart_FromWaiting_IsRejected()
    {
        var match = CreateMatch();

        Assert.Throws<InvalidMatchTransitionException>(() => match.CompleteStart(TransitionAt));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Finish_FromStarted_TransitionsToFinished()
    {
        var match = CreateStartedMatch();

        match.Finish(TransitionAt);

        Assert.Equal(MatchStatus.Finished, match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Finish_FromWaiting_IsRejected()
    {
        var match = CreateMatch();

        Assert.Throws<InvalidMatchTransitionException>(() => match.Finish(TransitionAt));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Cancel_ByOwnerFromWaiting_TransitionsToCancelled()
    {
        var match = CreateMatch();

        match.Cancel(OwnerId, TransitionAt);

        Assert.Equal(MatchStatus.Cancelled, match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Cancel_ByOwnerFromStarting_TransitionsToCancelled()
    {
        var match = CreateStartingMatch();

        match.Cancel(OwnerId, TransitionAt);

        Assert.Equal(MatchStatus.Cancelled, match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Cancel_ByOwnerFromStarted_IsRejected()
    {
        var match = CreateStartedMatch();

        Assert.Throws<InvalidMatchTransitionException>(() => match.Cancel(OwnerId, TransitionAt));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Cancel_ByNonOwner_IsRejected()
    {
        var match = CreateMatch();

        Assert.Throws<UnauthorizedAccessException>(() => match.Cancel("other-user", TransitionAt));
    }

    [Theory]
    [InlineData(MatchStatus.Waiting)]
    [InlineData(MatchStatus.Starting)]
    [InlineData(MatchStatus.Started)]
    [Trait("Category", "Unit")]
    public void Expire_FromActiveStatus_TransitionsToCancelled(MatchStatus initialStatus)
    {
        var match = CreateInStatus(initialStatus);

        match.Expire(TransitionAt);

        Assert.Equal(MatchStatus.Cancelled, match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Expire_FromFinished_IsRejected()
    {
        var match = CreateStartedMatch();
        match.Finish(CreatedAt.AddMinutes(3));

        Assert.Throws<InvalidMatchTransitionException>(() => match.Expire(TransitionAt));
    }

    [Theory]
    [InlineData("request-start")]
    [InlineData("complete-start")]
    [InlineData("finish")]
    [InlineData("cancel")]
    [InlineData("expire")]
    [Trait("Category", "Unit")]
    public void Cancelled_DoesNotAcceptAnotherTransition(string operation)
    {
        var match = CreateMatch();
        match.Cancel(OwnerId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidMatchTransitionException>(() => ApplyOperation(match, operation));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SuccessfulTransition_UsesProvidedTimestampForLastActivity()
    {
        var match = CreateMatch();

        match.RequestStart(OwnerId, TransitionAt);

        Assert.Equal(TransitionAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SuccessfulTransition_WithEarlierTimestamp_DoesNotMoveLastActivityBackward()
    {
        var match = CreateMatch();
        match.JoinParticipant("user-1", TransitionAt);

        match.RequestStart(OwnerId, CreatedAt.AddMinutes(2));

        Assert.Equal(MatchStatus.Starting, match.Status);
        Assert.Equal(TransitionAt, match.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RejectedTransition_DoesNotChangeStatusOrLastActivity()
    {
        var match = CreateMatch();

        Assert.Throws<InvalidMatchTransitionException>(() => match.Finish(TransitionAt));
        Assert.Equal(MatchStatus.Waiting, match.Status);
        Assert.Equal(CreatedAt, match.LastActivityAt);
    }

    private static Match CreateMatch() =>
        new("match-1", "Friday match", "Chess", OwnerId, CreatedAt, 2);

    private static Match CreateStartingMatch()
    {
        var match = CreateMatch();
        match.RequestStart(OwnerId, CreatedAt.AddMinutes(1));
        return match;
    }

    private static Match CreateStartedMatch()
    {
        var match = CreateStartingMatch();
        match.CompleteStart(CreatedAt.AddMinutes(2));
        return match;
    }

    private static Match CreateInStatus(MatchStatus status) => status switch
    {
        MatchStatus.Waiting => CreateMatch(),
        MatchStatus.Starting => CreateStartingMatch(),
        MatchStatus.Started => CreateStartedMatch(),
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static void ApplyOperation(Match match, string operation)
    {
        switch (operation)
        {
            case "request-start":
                match.RequestStart(OwnerId, TransitionAt);
                break;
            case "complete-start":
                match.CompleteStart(TransitionAt);
                break;
            case "finish":
                match.Finish(TransitionAt);
                break;
            case "cancel":
                match.Cancel(OwnerId, TransitionAt);
                break;
            case "expire":
                match.Expire(TransitionAt);
                break;
        }
    }
}
