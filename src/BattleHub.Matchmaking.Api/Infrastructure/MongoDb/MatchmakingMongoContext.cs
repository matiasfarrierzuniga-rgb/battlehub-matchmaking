using MongoDB.Driver;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb;

public sealed class MatchmakingMongoContext
{
    public MatchmakingMongoContext(IMongoDatabase database)
    {
        Database = database;
    }

    private IMongoDatabase Database { get; }
}
