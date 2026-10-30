namespace BattleHub.Matchmaking.Api.Application.Matches;

public static class SupportedGames
{
    public static bool IsSupported(string? gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType))
        {
            return false;
        }

        return gameType.Equals("typing", StringComparison.OrdinalIgnoreCase)
            || gameType.Equals("trivia", StringComparison.OrdinalIgnoreCase)
            || gameType.Equals("memory", StringComparison.OrdinalIgnoreCase);
    }
}
