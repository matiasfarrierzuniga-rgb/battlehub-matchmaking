namespace BattleHub.Matchmaking.Api.Application.Matches;

public sealed record CreateMatchCommand(string Title, string GameType, int MaxPlayers);
