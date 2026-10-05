namespace BattleHub.Matchmaking.Api.Transport.Lobby;

public static class LobbyGroups
{
    public const string Lobby = "lobby";

    public static string ForMatch(string matchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);

        return $"match:{matchId}";
    }
}
