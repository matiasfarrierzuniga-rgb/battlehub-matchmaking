using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace BattleHub.Matchmaking.Tests.Infrastructure.MongoDb.Matches;

public class MatchDocumentMapperTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastActivityAt = CreatedAt.AddMinutes(15);
    private static readonly DateTimeOffset JoinedAt = CreatedAt.AddMinutes(3);
    private static readonly DateTimeOffset LastHeartbeatAt = CreatedAt.AddMinutes(12);

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDocument_WaitingMatch_PreservesStateAndRevision()
    {
        var match = CreateMatch();

        var document = MatchDocumentMapper.ToDocument(match, 7);

        Assert.Equal(match.Id, document.Id);
        Assert.Equal(match.Title, document.Title);
        Assert.Equal(match.GameType, document.GameType);
        Assert.Equal(match.CreatedBy, document.CreatedBy);
        Assert.Equal(match.CreatedAt, document.CreatedAt);
        Assert.Equal(match.LastActivityAt, document.LastActivityAt);
        Assert.Equal(match.MaxPlayers, document.MaxPlayers);
        Assert.Equal(MatchStatus.Waiting, document.Status);
        Assert.Empty(document.Participants);
        Assert.Equal(7, document.Revision);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_PreservesDocumentState()
    {
        var document = CreateDocument(MatchStatus.Waiting, revision: 9);

        var stored = MatchDocumentMapper.ToDomain(document);

        Assert.Equal(document.Id, stored.Match.Id);
        Assert.Equal(document.Title, stored.Match.Title);
        Assert.Equal(document.GameType, stored.Match.GameType);
        Assert.Equal(document.CreatedBy, stored.Match.CreatedBy);
        Assert.Equal(document.CreatedAt, stored.Match.CreatedAt);
        Assert.Equal(document.LastActivityAt, stored.Match.LastActivityAt);
        Assert.Equal(document.MaxPlayers, stored.Match.MaxPlayers);
        Assert.Equal(document.Status, stored.Match.Status);
        Assert.Equal(document.Revision, stored.Revision);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_PreservesParticipantAndItsTimestamps()
    {
        var stored = MatchDocumentMapper.ToDomain(CreateDocument(MatchStatus.Waiting));
        var roundTripped = MatchDocumentMapper.ToDocument(stored.Match, stored.Revision);

        var participant = Assert.Single(roundTripped.Participants);
        Assert.Equal("user-1", participant.UserId);
        Assert.Equal(JoinedAt, participant.JoinedAt);
        Assert.Equal(LastHeartbeatAt, participant.LastHeartbeatAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_PreservesWaitingStatus()
    {
        var stored = MatchDocumentMapper.ToDomain(CreateDocument(MatchStatus.Waiting));

        var roundTripped = MatchDocumentMapper.ToDocument(stored.Match, stored.Revision);

        Assert.Equal(MatchStatus.Waiting, roundTripped.Status);
    }

    [Theory]
    [InlineData(MatchStatus.Starting)]
    [InlineData(MatchStatus.Started)]
    [InlineData(MatchStatus.Finished)]
    [InlineData(MatchStatus.Cancelled)]
    [Trait("Category", "Unit")]
    public void ToDomain_RehydratesExactNonWaitingStatus(MatchStatus status)
    {
        var stored = MatchDocumentMapper.ToDomain(CreateDocument(status));

        Assert.Equal(status, stored.Match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rehydrate_DoesNotModifyTimestamps()
    {
        var match = Match.Rehydrate(
            "match-1",
            "Friday match",
            "Chess",
            "owner-1",
            CreatedAt,
            LastActivityAt,
            2,
            MatchStatus.Started,
            [new MatchParticipant("user-1", JoinedAt, LastHeartbeatAt)]);

        var participant = Assert.Single(match.Participants);
        Assert.Equal(CreatedAt, match.CreatedAt);
        Assert.Equal(LastActivityAt, match.LastActivityAt);
        Assert.Equal(JoinedAt, participant.JoinedAt);
        Assert.Equal(LastHeartbeatAt, participant.LastHeartbeatAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rehydrate_WithDuplicateParticipants_IsRejected()
    {
        var participants = new[]
        {
            new MatchParticipant("user-1", JoinedAt, LastHeartbeatAt),
            new MatchParticipant("user-1", JoinedAt.AddMinutes(1), LastHeartbeatAt)
        };

        Assert.Throws<ArgumentException>(() => Match.Rehydrate(
            "match-1", "Friday match", "Chess", "owner-1", CreatedAt, LastActivityAt,
            2, MatchStatus.Waiting, participants));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rehydrate_WithMoreParticipantsThanCapacity_IsRejected()
    {
        var participants = new[]
        {
            new MatchParticipant("user-1", JoinedAt, LastHeartbeatAt),
            new MatchParticipant("user-2", JoinedAt, LastHeartbeatAt)
        };

        Assert.Throws<ArgumentException>(() => Match.Rehydrate(
            "match-1", "Friday match", "Chess", "owner-1", CreatedAt, LastActivityAt,
            1, MatchStatus.Waiting, participants));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Match_DoesNotExposeRevision()
    {
        Assert.Null(typeof(Match).GetProperty("Revision"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Document_SerializesTimesAsBsonDatesAndStatusAsEnumValue()
    {
        var bson = CreateDocument(MatchStatus.Started).ToBsonDocument();

        Assert.Equal(BsonType.DateTime, bson[nameof(MatchDocument.CreatedAt)].BsonType);
        Assert.Equal(BsonType.DateTime, bson[nameof(MatchDocument.LastActivityAt)].BsonType);
        Assert.Equal(BsonType.Int32, bson[nameof(MatchDocument.Status)].BsonType);

        var participant = bson[nameof(MatchDocument.Participants)].AsBsonArray[0].AsBsonDocument;
        Assert.Equal(BsonType.DateTime, participant[nameof(MatchParticipantDocument.JoinedAt)].BsonType);
        Assert.Equal(BsonType.DateTime, participant[nameof(MatchParticipantDocument.LastHeartbeatAt)].BsonType);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BsonRoundTrip_PreservesPersistedMatchState()
    {
        var document = CreateDocument(MatchStatus.Started, revision: 11);

        var deserialized = BsonSerializer.Deserialize<MatchDocument>(document.ToBson());

        Assert.Equal(document.Id, deserialized.Id);
        Assert.Equal(MatchStatus.Started, deserialized.Status);
        Assert.Equal(11, deserialized.Revision);
        Assert.Equal(document.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(document.LastActivityAt, deserialized.LastActivityAt);

        var participant = Assert.Single(deserialized.Participants);
        Assert.Equal("user-1", participant.UserId);
        Assert.Equal(JoinedAt, participant.JoinedAt);
        Assert.Equal(LastHeartbeatAt, participant.LastHeartbeatAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MatchToBsonRoundTrip_NormalizesNonUtcTimestampsToUtc()
    {
        var offset = TimeSpan.FromHours(-6);
        var createdAt = new DateTimeOffset(2026, 9, 30, 6, 0, 0, offset);
        var lastActivityAt = createdAt.AddMinutes(15);
        var joinedAt = createdAt.AddMinutes(3);
        var lastHeartbeatAt = createdAt.AddMinutes(12);
        var match = Match.Rehydrate(
            "match-offset",
            "Offset match",
            "Chess",
            "owner-1",
            createdAt,
            lastActivityAt,
            4,
            MatchStatus.Started,
            [new MatchParticipant("user-offset", joinedAt, lastHeartbeatAt)]);
        var document = MatchDocumentMapper.ToDocument(match, revision: 12);

        var deserialized = BsonSerializer.Deserialize<MatchDocument>(document.ToBson());

        Assert.Equal("match-offset", deserialized.Id);
        Assert.Equal(MatchStatus.Started, deserialized.Status);
        Assert.Equal(12, deserialized.Revision);
        Assert.Equal(createdAt.ToUniversalTime(), deserialized.CreatedAt);
        Assert.Equal(lastActivityAt.ToUniversalTime(), deserialized.LastActivityAt);
        Assert.Equal(TimeSpan.Zero, deserialized.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, deserialized.LastActivityAt.Offset);

        var participant = Assert.Single(deserialized.Participants);
        Assert.Equal("user-offset", participant.UserId);
        Assert.Equal(joinedAt.ToUniversalTime(), participant.JoinedAt);
        Assert.Equal(lastHeartbeatAt.ToUniversalTime(), participant.LastHeartbeatAt);
        Assert.Equal(TimeSpan.Zero, participant.JoinedAt.Offset);
        Assert.Equal(TimeSpan.Zero, participant.LastHeartbeatAt.Offset);
    }

    private static Match CreateMatch() =>
        new("match-1", "Friday match", "Chess", "owner-1", CreatedAt, 4);

    private static MatchDocument CreateDocument(MatchStatus status, long revision = 4) => new()
    {
        Id = "match-1",
        Title = "Friday match",
        GameType = "Chess",
        CreatedBy = "owner-1",
        CreatedAt = CreatedAt,
        LastActivityAt = LastActivityAt,
        MaxPlayers = 4,
        Status = status,
        Revision = revision,
        Participants =
        [
            new MatchParticipantDocument
            {
                UserId = "user-1",
                JoinedAt = JoinedAt,
                LastHeartbeatAt = LastHeartbeatAt
            }
        ]
    };
}
