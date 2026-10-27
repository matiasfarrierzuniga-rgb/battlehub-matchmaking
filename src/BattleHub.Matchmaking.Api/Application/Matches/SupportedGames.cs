namespace BattleHub.Matchmaking.Api.Application.Matches;

public static class SupportedGames
{
    public const string CreatePermission = "matches.create";

    public static bool TryGetPlayPermission(string? gameType, out string permission)
    {
        if (string.IsNullOrWhiteSpace(gameType))
        {
            permission = string.Empty;
            return false;
        }

        permission = gameType.ToLowerInvariant() switch
        {
            "typing" => "games.typing.play",
            "trivia" => "games.trivia.play",
            "memory" => "games.memory.play",
            _ => string.Empty
        };

        return permission.Length > 0;
    }
}
