namespace BattleHub.Matchmaking.Api.Transport.Matches;

public sealed record MatchHistoryResponse(
    string MatchId,
    string GameType,
    string Status,
    int MaxPlayers,
    int ParticipantCount,
    bool IsHost,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    DateTime? CancelledAt);

public sealed record MatchHistoryPageResponse(
    MatchHistoryResponse[] Items,
    int Page,
    int PageSize,
    int TotalCount);
