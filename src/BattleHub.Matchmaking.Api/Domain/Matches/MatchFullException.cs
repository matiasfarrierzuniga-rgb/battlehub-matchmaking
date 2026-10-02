namespace BattleHub.Matchmaking.Api.Domain.Matches;

public sealed class MatchFullException : InvalidOperationException
{
    public MatchFullException(string matchId, int maxPlayers)
        : base($"Match '{matchId}' is full and cannot exceed its capacity of {maxPlayers} players.")
    {
        MatchId = matchId;
        MaxPlayers = maxPlayers;
    }

    public string MatchId { get; }

    public int MaxPlayers { get; }
}
