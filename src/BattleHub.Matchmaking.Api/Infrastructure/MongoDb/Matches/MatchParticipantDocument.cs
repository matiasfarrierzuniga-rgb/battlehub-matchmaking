using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;

public sealed class MatchParticipantDocument
{
    public required string UserId { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    public DateTimeOffset JoinedAt { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    public DateTimeOffset LastHeartbeatAt { get; init; }
}
