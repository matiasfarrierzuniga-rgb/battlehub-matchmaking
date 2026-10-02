namespace BattleHub.Matchmaking.Api.Transport.Matches;

public sealed record CreateMatchRequest(string Title, string GameType, int MaxPlayers);
