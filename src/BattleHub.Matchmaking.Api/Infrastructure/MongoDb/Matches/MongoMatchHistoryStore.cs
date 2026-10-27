using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using MongoDB.Driver;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;

public sealed class MongoMatchHistoryStore : IMatchHistoryStore
{
    private readonly IMongoCollection<MatchHistoryDocument> _history;
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private bool _indexReady;

    public MongoMatchHistoryStore(MatchmakingMongoContext context)
    {
        _history = context.History;
    }

    public async Task RecordAsync(Match match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (!MatchHistory.Qualifies(match))
        {
            return;
        }

        await EnsureIndexAsync(cancellationToken);
        var document = ToDocument(match);
        await _history.ReplaceOneAsync(
            item => item.Id == document.Id,
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<MatchHistoryPage> ListAsync(
        string userId,
        string? gameType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        MatchHistory.EnsurePaging(page, pageSize);
        var normalizedGameType = MatchHistory.NormalizeGameType(gameType);
        await EnsureIndexAsync(cancellationToken);

        var filters = new List<FilterDefinition<MatchHistoryDocument>>
        {
            Builders<MatchHistoryDocument>.Filter.AnyEq(item => item.ParticipantIds, userId)
        };
        if (normalizedGameType is not null)
        {
            filters.Add(Builders<MatchHistoryDocument>.Filter.Eq(item => item.GameType, normalizedGameType));
        }

        var filter = Builders<MatchHistoryDocument>.Filter.And(filters);
        var total = await _history.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var documents = await _history.Find(filter)
            .Sort(Builders<MatchHistoryDocument>.Sort
                .Descending(item => item.RecordedAt)
                .Descending(item => item.Id))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new MatchHistoryPage(
            documents.Select(document => ToItem(document, userId)).ToArray(),
            page,
            pageSize,
            checked((int)total));
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (_indexReady)
        {
            return;
        }

        await _indexGate.WaitAsync(cancellationToken);
        try
        {
            if (_indexReady)
            {
                return;
            }

            await _history.Indexes.CreateOneAsync(
                new CreateIndexModel<MatchHistoryDocument>(
                    Builders<MatchHistoryDocument>.IndexKeys
                        .Ascending(item => item.ParticipantIds)
                        .Descending(item => item.RecordedAt)),
                cancellationToken: cancellationToken);
            _indexReady = true;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private static MatchHistoryDocument ToDocument(Match match) => new()
    {
        Id = match.Id,
        GameType = match.GameType.ToLowerInvariant(),
        Status = match.Status.ToString(),
        MaxPlayers = match.MaxPlayers,
        ParticipantCount = match.CurrentPlayers,
        CreatedBy = match.CreatedBy,
        ParticipantIds = match.Participants.Select(participant => participant.UserId).ToArray(),
        CreatedAt = match.CreatedAt.ToUniversalTime(),
        StartedAt = match.StartedAt?.ToUniversalTime(),
        FinishedAt = match.FinishedAt?.ToUniversalTime(),
        CancelledAt = match.CancelledAt?.ToUniversalTime(),
        RecordedAt = MatchHistory.RecordedAt(match).ToUniversalTime()
    };

    private static MatchHistoryItem ToItem(MatchHistoryDocument document, string userId) => new(
        document.Id,
        document.GameType,
        document.Status,
        document.MaxPlayers,
        document.ParticipantCount,
        string.Equals(document.CreatedBy, userId, StringComparison.Ordinal),
        document.CreatedAt,
        document.StartedAt,
        document.FinishedAt,
        document.CancelledAt);
}
