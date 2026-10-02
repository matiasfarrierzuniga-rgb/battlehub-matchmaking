using BattleHub.Matchmaking.Api.Domain.Matches;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;

public sealed class MatchDocument
{
    [BsonId]
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string GameType { get; init; }

    public required string CreatedBy { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    public DateTimeOffset CreatedAt { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    public DateTimeOffset LastActivityAt { get; init; }

    public int MaxPlayers { get; init; }

    public MatchStatus Status { get; init; }

    public long Revision { get; init; }

    public required IReadOnlyList<MatchParticipantDocument> Participants { get; init; }
}
