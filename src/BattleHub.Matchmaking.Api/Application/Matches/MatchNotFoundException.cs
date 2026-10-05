namespace BattleHub.Matchmaking.Api.Application.Matches;

public sealed class MatchNotFoundException(string matchId)
    : KeyNotFoundException($"Match '{matchId}' was not found.")
{
    public string MatchId { get; } = matchId;
}
