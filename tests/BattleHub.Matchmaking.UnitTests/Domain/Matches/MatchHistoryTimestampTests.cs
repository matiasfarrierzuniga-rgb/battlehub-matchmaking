using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace BattleHub.Matchmaking.Tests.Domain.Matches;

public class MatchHistoryTimestampTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private const string OwnerId = "owner-1";

    [Fact]
    [Trait("Category", "Unit")]
    public void CompleteStart_RecordsStartedAtWithoutChangingCreatedAt()
    {
        var match = Ready("typing", 4);
        var startedAt = CreatedAt.AddMinutes(5);

        match.CompleteStart(startedAt);

        Assert.Equal(CreatedAt, match.CreatedAt);
        Assert.Equal(startedAt, match.StartedAt);
        Assert.Null(match.FinishedAt);
        Assert.Null(match.CancelledAt);
        Assert.False(MatchHistory.Qualifies(match));
    }

    [Theory]
    [InlineData("typing", 4)]
    [InlineData("trivia", 4)]
    [InlineData("memory", 2)]
    [Trait("Category", "Unit")]
    public void Finish_RecordsFinishedAt(string gameType, int maxPlayers)
    {
        var match = Ready(gameType, maxPlayers);
        match.CompleteStart(CreatedAt.AddMinutes(1));
        var finishedAt = CreatedAt.AddMinutes(10);

        match.Finish(finishedAt);

        Assert.Equal(finishedAt, match.FinishedAt);
        Assert.Equal(CreatedAt, match.CreatedAt);
        Assert.True(MatchHistory.Qualifies(match));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CancelledWaitingRoom_IsNotPlayedHistory()
    {
        var match = new Match("match-1", "Lobby", "typing", OwnerId, CreatedAt, 4);
        match.Cancel(OwnerId, CreatedAt.AddMinutes(2));

        Assert.Equal(CreatedAt.AddMinutes(2), match.CancelledAt);
        Assert.Null(match.StartedAt);
        Assert.False(MatchHistory.Qualifies(match));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExpiredStartedMatch_IsPlayedHistory()
    {
        var match = Ready("memory", 2);
        match.CompleteStart(CreatedAt.AddMinutes(1));
        var cancelledAt = CreatedAt.AddMinutes(30);

        match.Expire(cancelledAt);

        Assert.Equal(cancelledAt, match.CancelledAt);
        Assert.Equal(CreatedAt.AddMinutes(1), match.StartedAt);
        Assert.True(MatchHistory.Qualifies(match));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OldMatchDocument_WithoutLifecycleTimestamps_StillLoads()
    {
        var bson = new BsonDocument
        {
            { "_id", "old-match" },
            { "Title", "Old room" },
            { "GameType", "typing" },
            { "CreatedBy", "owner-1" },
            { "CreatedAt", new BsonDateTime(CreatedAt.UtcDateTime) },
            { "LastActivityAt", new BsonDateTime(CreatedAt.UtcDateTime) },
            { "MaxPlayers", 4 },
            { "Status", (int)MatchStatus.Finished },
            { "Revision", 2L },
            { "Participants", new BsonArray() }
        };

        var document = BsonSerializer.Deserialize<MatchDocument>(bson);
        var stored = MatchDocumentMapper.ToDomain(document);

        Assert.Equal(MatchStatus.Finished, stored.Match.Status);
        Assert.Null(stored.Match.StartedAt);
        Assert.Null(stored.Match.FinishedAt);
        Assert.Null(stored.Match.CancelledAt);
    }

    private static Match Ready(string gameType, int maxPlayers)
    {
        var match = new Match("match-1", "Friday", gameType, OwnerId, CreatedAt, maxPlayers);
        match.JoinParticipant(OwnerId, CreatedAt);
        match.JoinParticipant("user-2", CreatedAt);
        match.RequestStart(OwnerId, CreatedAt.AddSeconds(30));
        return match;
    }
}
