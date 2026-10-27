namespace BattleHub.Matchmaking.Api.Domain.Matches;

public static class GameCapacity
{
    public const int MemoryMaxPlayers = 2;

    public static void Ensure(string gameType, int requestedMaxPlayers)
    {
        if (string.Equals(gameType, "memory", StringComparison.OrdinalIgnoreCase)
            && requestedMaxPlayers != MemoryMaxPlayers)
        {
            throw new ArgumentException("Memory requires exactly 2 players.");
        }
    }
}
