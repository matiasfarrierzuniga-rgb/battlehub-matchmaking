using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;

public sealed class MatchHistoryDocument
{
    [BsonId]
    public required string Id { get; init; }

    public required string GameType { get; init; }

    public required string Status { get; init; }

    public int MaxPlayers { get; init; }

    public int ParticipantCount { get; init; }

    public required string CreatedBy { get; init; }

    public required string[] ParticipantIds { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    public DateTimeOffset CreatedAt { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    [BsonIgnoreIfNull]
    public DateTimeOffset? StartedAt { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    [BsonIgnoreIfNull]
    public DateTimeOffset? FinishedAt { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    [BsonIgnoreIfNull]
    public DateTimeOffset? CancelledAt { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    public DateTimeOffset RecordedAt { get; init; }
}
