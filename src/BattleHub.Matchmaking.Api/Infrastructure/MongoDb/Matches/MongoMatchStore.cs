using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;

public sealed class MongoMatchStore(MatchmakingMongoContext context) : IMatchStore
{
    public async Task<DateTimeOffset> GetDatabaseTimeAsync(CancellationToken cancellationToken)
    {
        var response = await context.GetServerHelloAsync(cancellationToken);

        if (!response.TryGetValue("localTime", out var value) || value.BsonType != BsonType.DateTime)
        {
            throw new InvalidOperationException("MongoDB did not return localTime from the hello command.");
        }

        return new DateTimeOffset(value.AsBsonDateTime.ToUniversalTime(), TimeSpan.Zero);
    }

    public async Task<StoredMatch?> GetAsync(string matchId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);

        var document = await context.Matches
            .Find(item => item.Id == matchId)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : MatchDocumentMapper.ToDomain(document);
    }

    public async Task<IReadOnlyList<StoredMatch>> ListAsync(CancellationToken cancellationToken)
    {
        var documents = await context.Matches
            .Find(FilterDefinition<MatchDocument>.Empty)
            .ToListAsync(cancellationToken);

        return documents.Select(MatchDocumentMapper.ToDomain).ToArray();
    }

    public Task InsertAsync(Match match, CancellationToken cancellationToken) =>
        context.Matches.InsertOneAsync(
            MatchDocumentMapper.ToDocument(match, revision: 0),
            cancellationToken: cancellationToken);

    public async Task<bool> TryReplaceAsync(
        Match match,
        long expectedRevision,
        MatchStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);

        var filter = Builders<MatchDocument>.Filter.And(
            Builders<MatchDocument>.Filter.Eq(document => document.Id, match.Id),
            Builders<MatchDocument>.Filter.Eq(document => document.Revision, expectedRevision),
            Builders<MatchDocument>.Filter.Eq(document => document.Status, expectedStatus));

        var replacement = MatchDocumentMapper.ToDocument(match, checked(expectedRevision + 1));
        var result = await context.Matches.ReplaceOneAsync(filter, replacement, cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }
}
