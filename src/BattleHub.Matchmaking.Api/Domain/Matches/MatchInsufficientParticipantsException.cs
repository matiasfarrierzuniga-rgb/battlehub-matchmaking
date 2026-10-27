namespace BattleHub.Matchmaking.Api.Domain.Matches;

public sealed class MatchInsufficientParticipantsException : InvalidOperationException
{
    public MatchInsufficientParticipantsException(string matchId, int currentPlayers, int minimumPlayers)
        : base($"Match '{matchId}' cannot start with {currentPlayers} participants. At least {minimumPlayers} participants are required.")
    {
        MatchId = matchId;
        CurrentPlayers = currentPlayers;
        MinimumPlayers = minimumPlayers;
    }

    public string MatchId { get; }

    public int CurrentPlayers { get; }

    public int MinimumPlayers { get; }
}
