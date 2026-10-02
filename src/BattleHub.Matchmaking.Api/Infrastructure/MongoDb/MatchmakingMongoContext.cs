using BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb;

public sealed class MatchmakingMongoContext
{
    public MatchmakingMongoContext(IMongoDatabase database)
    {
        Database = database;
    }

    public IMongoCollection<MatchDocument> Matches => Database.GetCollection<MatchDocument>("matches");

    internal Task<BsonDocument> GetServerHelloAsync(CancellationToken cancellationToken) =>
        Database.RunCommandAsync<BsonDocument>(
            new BsonDocument("hello", 1),
            cancellationToken: cancellationToken);

    private IMongoDatabase Database { get; }
}
