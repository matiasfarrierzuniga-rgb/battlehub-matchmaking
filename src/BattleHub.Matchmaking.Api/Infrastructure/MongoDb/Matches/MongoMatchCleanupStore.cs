using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Application.Matches.Cleanup;
using BattleHub.Matchmaking.Api.Domain.Matches;
using MongoDB.Driver;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;

public sealed class MongoMatchCleanupStore(MatchmakingMongoContext context) : IMatchCleanupStore
{
    public Task<bool> TryCancelInactiveWaitingAsync(
        StoredMatch stored,
        DateTimeOffset databaseNow,
        DateTimeOffset inactivityCutoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return stored.Match.Status != MatchStatus.Waiting
            ? Task.FromResult(false)
            : TryCancelAsync(
            stored,
            databaseNow,
            MongoMatchCleanupOperations.BuildInactiveWaitingFilter(stored, inactivityCutoff),
            cancellationToken);
    }

    public Task<bool> TryCancelWithoutValidHeartbeatAsync(
        StoredMatch stored,
        DateTimeOffset databaseNow,
        DateTimeOffset heartbeatCutoff,
        CancellationToken cancellationToken) =>
        TryCancelAsync(
            stored,
            databaseNow,
            MongoMatchCleanupOperations.BuildNoValidHeartbeatFilter(stored, heartbeatCutoff),
            cancellationToken);

    public Task<bool> TryCancelExpiredAsync(
        StoredMatch stored,
        DateTimeOffset databaseNow,
        DateTimeOffset expirationCutoff,
        CancellationToken cancellationToken) =>
        TryCancelAsync(
            stored,
            databaseNow,
            MongoMatchCleanupOperations.BuildExpiredFilter(stored, expirationCutoff),
            cancellationToken);

    public async Task<bool> TryDeleteTerminalAsync(
        StoredMatch stored,
        DateTimeOffset retentionCutoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.Match.Status is not (MatchStatus.Finished or MatchStatus.Cancelled))
        {
            return false;
        }

        var result = await context.Matches.DeleteOneAsync(
            MongoMatchCleanupOperations.BuildTerminalRetentionFilter(stored, retentionCutoff),
            cancellationToken);

        return result.DeletedCount == 1;
    }

    private async Task<bool> TryCancelAsync(
        StoredMatch stored,
        DateTimeOffset databaseNow,
        FilterDefinition<MatchDocument> filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.Match.Status is not (MatchStatus.Waiting or MatchStatus.Starting or MatchStatus.Started))
        {
            return false;
        }

        var replacement = MongoMatchCleanupOperations.BuildCancellationReplacement(stored, databaseNow);
        var result = await context.Matches.ReplaceOneAsync(filter, replacement, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }
}

internal static class MongoMatchCleanupOperations
{
    private static readonly FilterDefinitionBuilder<MatchDocument> Filters = Builders<MatchDocument>.Filter;

    internal static FilterDefinition<MatchDocument> BuildInactiveWaitingFilter(
        StoredMatch stored,
        DateTimeOffset inactivityCutoff)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return Filters.And(
            BuildIdentityFilter(stored),
            Filters.Eq(document => document.Status, MatchStatus.Waiting),
            Filters.Lte(document => document.LastActivityAt, inactivityCutoff));
    }

    internal static FilterDefinition<MatchDocument> BuildNoValidHeartbeatFilter(
        StoredMatch stored,
        DateTimeOffset heartbeatCutoff)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return Filters.And(
            BuildIdentityFilter(stored),
            Filters.Eq(document => document.Status, stored.Match.Status),
            Filters.Exists($"{nameof(MatchDocument.Participants)}.0"),
            Filters.Not(Filters.ElemMatch(
                document => document.Participants,
                participant => participant.LastHeartbeatAt > heartbeatCutoff)));
    }

    internal static FilterDefinition<MatchDocument> BuildExpiredFilter(
        StoredMatch stored,
        DateTimeOffset expirationCutoff)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return Filters.And(
            BuildIdentityFilter(stored),
            Filters.Eq(document => document.Status, stored.Match.Status),
            Filters.Lte(document => document.CreatedAt, expirationCutoff));
    }

    internal static FilterDefinition<MatchDocument> BuildTerminalRetentionFilter(
        StoredMatch stored,
        DateTimeOffset retentionCutoff)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return Filters.And(
            BuildIdentityFilter(stored),
            Filters.Eq(document => document.Status, stored.Match.Status),
            Filters.Lte(document => document.LastActivityAt, retentionCutoff));
    }

    internal static MatchDocument BuildCancellationReplacement(StoredMatch stored, DateTimeOffset databaseNow)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var source = stored.Match;
        var cancellation = Match.Rehydrate(
            source.Id,
            source.Title,
            source.GameType,
            source.CreatedBy,
            source.CreatedAt,
            source.LastActivityAt,
            source.MaxPlayers,
            source.Status,
            source.Participants.Select(participant => new MatchParticipant(
                participant.UserId,
                participant.JoinedAt,
                participant.LastHeartbeatAt)));

        cancellation.Expire(databaseNow);
        return MatchDocumentMapper.ToDocument(cancellation, checked(stored.Revision + 1));
    }

    private static FilterDefinition<MatchDocument> BuildIdentityFilter(StoredMatch stored) =>
        Filters.And(
            Filters.Eq(document => document.Id, stored.Match.Id),
            Filters.Eq(document => document.Revision, stored.Revision));
}
