namespace BattleHub.Matchmaking.Api.Domain.Matches;

public sealed class MatchMembershipChangeNotAllowedException(MatchStatus currentStatus)
    : InvalidOperationException($"Match membership cannot change while the match is {currentStatus}.")
{
    public MatchStatus CurrentStatus { get; } = currentStatus;
}
