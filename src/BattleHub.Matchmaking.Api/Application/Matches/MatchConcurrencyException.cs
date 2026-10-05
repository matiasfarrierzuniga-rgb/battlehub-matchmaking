namespace BattleHub.Matchmaking.Api.Application.Matches;

public sealed class MatchConcurrencyException(string matchId)
    : InvalidOperationException($"Match '{matchId}' could not be updated because of concurrent changes.")
{
    public string MatchId { get; } = matchId;
}
