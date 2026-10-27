using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Support;

internal sealed class MemoryMatchHistoryStore : IMatchHistoryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public Task RecordAsync(Match match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (!MatchHistory.Qualifies(match))
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            _entries[match.Id] = Entry.From(match);
        }

        return Task.CompletedTask;
    }

    public Task<MatchHistoryPage> ListAsync(
        string userId,
        string? gameType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        MatchHistory.EnsurePaging(page, pageSize);
        var normalizedGameType = MatchHistory.NormalizeGameType(gameType);

        lock (_gate)
        {
            var matches = _entries.Values
                .Where(entry => entry.ParticipantIds.Contains(userId, StringComparer.Ordinal))
                .Where(entry => normalizedGameType is null
                    || string.Equals(entry.GameType, normalizedGameType, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.RecordedAt)
                .ThenByDescending(entry => entry.MatchId, StringComparer.Ordinal)
                .ToArray();

            var pageItems = matches
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(entry => entry.ToItem(userId))
                .ToArray();

            return Task.FromResult(new MatchHistoryPage(pageItems, page, pageSize, matches.Length));
        }
    }

    private sealed record Entry(
        string MatchId,
        string GameType,
        string Status,
        int MaxPlayers,
        int ParticipantCount,
        string CreatedBy,
        string[] ParticipantIds,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? FinishedAt,
        DateTimeOffset? CancelledAt,
        DateTimeOffset RecordedAt)
    {
        public static Entry From(Match match) => new(
            match.Id,
            match.GameType.ToLowerInvariant(),
            match.Status.ToString(),
            match.MaxPlayers,
            match.CurrentPlayers,
            match.CreatedBy,
            match.Participants.Select(participant => participant.UserId).ToArray(),
            match.CreatedAt,
            match.StartedAt,
            match.FinishedAt,
            match.CancelledAt,
            MatchHistory.RecordedAt(match));

        public MatchHistoryItem ToItem(string userId) => new(
            MatchId,
            GameType,
            Status,
            MaxPlayers,
            ParticipantCount,
            string.Equals(CreatedBy, userId, StringComparison.Ordinal),
            CreatedAt,
            StartedAt,
            FinishedAt,
            CancelledAt);
    }
}
