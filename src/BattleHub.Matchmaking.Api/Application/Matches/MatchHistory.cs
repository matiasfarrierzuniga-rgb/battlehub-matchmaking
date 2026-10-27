using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Application.Matches;

public static class MatchHistory
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public static bool Qualifies(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return match.Status == MatchStatus.Finished
            || (match.Status == MatchStatus.Cancelled && match.StartedAt is not null);
    }

    public static DateTimeOffset RecordedAt(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.FinishedAt ?? match.CancelledAt ?? match.StartedAt ?? match.CreatedAt;
    }

    public static string? NormalizeGameType(string? gameType)
    {
        if (gameType is null)
        {
            return null;
        }

        if (!SupportedGames.TryGetPlayPermission(gameType, out _))
        {
            throw new ArgumentException(
                string.IsNullOrWhiteSpace(gameType)
                    ? "Game type is required."
                    : $"'{gameType}' is not a supported game type.");
        }

        return gameType.ToLowerInvariant();
    }

    public static void EnsurePaging(int page, int pageSize)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be at least 1.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                $"Page size must be between 1 and {MaxPageSize}.");
        }
    }
}

public sealed record MatchHistoryItem(
    string MatchId,
    string GameType,
    string Status,
    int MaxPlayers,
    int ParticipantCount,
    bool IsHost,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? CancelledAt);

public sealed record MatchHistoryPage(
    IReadOnlyList<MatchHistoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public interface IMatchHistoryStore
{
    Task RecordAsync(Match match, CancellationToken cancellationToken);

    Task<MatchHistoryPage> ListAsync(
        string userId,
        string? gameType,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
