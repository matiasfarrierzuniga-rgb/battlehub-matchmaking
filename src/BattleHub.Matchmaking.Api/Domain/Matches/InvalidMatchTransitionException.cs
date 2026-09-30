namespace BattleHub.Matchmaking.Api.Domain.Matches;

public sealed class InvalidMatchTransitionException : InvalidOperationException
{
    public InvalidMatchTransitionException(MatchStatus currentStatus, MatchStatus targetStatus)
        : base($"A match cannot transition from {currentStatus} to {targetStatus}.")
    {
        CurrentStatus = currentStatus;
        TargetStatus = targetStatus;
    }

    public MatchStatus CurrentStatus { get; }

    public MatchStatus TargetStatus { get; }
}
