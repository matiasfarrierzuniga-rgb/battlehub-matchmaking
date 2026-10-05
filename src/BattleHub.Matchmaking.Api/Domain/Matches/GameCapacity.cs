namespace BattleHub.Matchmaking.Api.Domain.Matches;

public static class GameCapacity
{
    public const int MemoryMaxPlayers = 2;

    public static int Apply(string gameType, int requestedMaxPlayers)
    {
        if (string.Equals(gameType, "memory", StringComparison.OrdinalIgnoreCase)
            && requestedMaxPlayers > MemoryMaxPlayers)
        {
            return MemoryMaxPlayers;
        }

        return requestedMaxPlayers;
    }
}
