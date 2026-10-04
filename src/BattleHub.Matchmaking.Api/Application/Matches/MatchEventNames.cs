namespace BattleHub.Matchmaking.Api.Application.Matches;

public static class MatchEventNames
{
    public const string MatchCreated = nameof(MatchCreated);
    public const string PlayerJoined = nameof(PlayerJoined);
    public const string PlayerLeft = nameof(PlayerLeft);
    public const string MatchStarting = nameof(MatchStarting);
    public const string MatchStarted = nameof(MatchStarted);
    public const string MatchFinished = nameof(MatchFinished);
    public const string MatchDeleted = nameof(MatchDeleted);
}
