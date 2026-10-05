using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace BattleHub.Matchmaking.Tests.Infrastructure.MongoDb.Matches;

public class MongoMatchCleanupOperationsTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = CreatedAt.AddHours(1);

    [Fact]
    [Trait("Category", "Unit")]
    public void InactiveFilter_RequiresIdentityRevisionWaitingAndActivityCutoff()
    {
        var filter = Render(MongoMatchCleanupOperations.BuildInactiveWaitingFilter(
            CreateStored(MatchStatus.Waiting, revision: 17), Cutoff));

        Assert.Equal("match-1", filter["_id"].AsString);
        Assert.Equal(17, filter[nameof(MatchDocument.Revision)].AsInt64);
        Assert.Equal((int)MatchStatus.Waiting, filter[nameof(MatchDocument.Status)].AsInt32);
        Assert.Equal(Cutoff.UtcDateTime, filter[nameof(MatchDocument.LastActivityAt)]["$lte"].ToUniversalTime());
    }

    [Theory]
    [InlineData(MatchStatus.Waiting)]
    [InlineData(MatchStatus.Starting)]
    [InlineData(MatchStatus.Started)]
    [Trait("Category", "Unit")]
    public void ExpiredFilter_PreservesExpectedActiveStatusAndRequiresCreatedCutoff(MatchStatus status)
    {
        var filter = Render(MongoMatchCleanupOperations.BuildExpiredFilter(CreateStored(status, 23), Cutoff));

        Assert.Equal(23, filter[nameof(MatchDocument.Revision)].AsInt64);
        Assert.Equal((int)status, filter[nameof(MatchDocument.Status)].AsInt32);
        Assert.Equal(Cutoff.UtcDateTime, filter[nameof(MatchDocument.CreatedAt)]["$lte"].ToUniversalTime());
    }

    [Theory]
    [InlineData(MatchStatus.Finished)]
    [InlineData(MatchStatus.Cancelled)]
    [Trait("Category", "Unit")]
    public void TerminalRetentionFilter_PreservesExpectedTerminalStatusAndRequiresActivityCutoff(MatchStatus status)
    {
        var filter = Render(MongoMatchCleanupOperations.BuildTerminalRetentionFilter(
            CreateStored(status, revision: 31), Cutoff));

        Assert.Equal(31, filter[nameof(MatchDocument.Revision)].AsInt64);
        Assert.Equal((int)status, filter[nameof(MatchDocument.Status)].AsInt32);
        Assert.Equal(Cutoff.UtcDateTime, filter[nameof(MatchDocument.LastActivityAt)]["$lte"].ToUniversalTime());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeartbeatFilter_RequiresParticipantAndRejectsHeartbeatAfterCutoff()
    {
        var filter = Render(MongoMatchCleanupOperations.BuildNoValidHeartbeatFilter(
            CreateStored(MatchStatus.Started, revision: 41), Cutoff));

        Assert.Equal(41, filter[nameof(MatchDocument.Revision)].AsInt64);
        Assert.Equal((int)MatchStatus.Started, filter[nameof(MatchDocument.Status)].AsInt32);
        Assert.True(filter[$"{nameof(MatchDocument.Participants)}.0"]["$exists"].AsBoolean);

        var heartbeatCondition = filter[nameof(MatchDocument.Participants)]["$not"]["$elemMatch"]
            [nameof(MatchParticipantDocument.LastHeartbeatAt)]["$gt"];
        Assert.Equal(Cutoff.UtcDateTime, heartbeatCondition.ToUniversalTime());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CancellationReplacement_ExpiresIncrementsRevisionAndKeepsActivityMonotonic()
    {
        var lastActivityAt = CreatedAt.AddHours(3);
        var stored = CreateStored(
            MatchStatus.Started,
            revision: 7,
            lastActivityAt: lastActivityAt,
            userId: "auth0|123",
            displayName: "Matias");

        var replacement = MongoMatchCleanupOperations.BuildCancellationReplacement(
            stored, CreatedAt.AddHours(2));

        var participant = Assert.Single(replacement.Participants);
        Assert.Equal(MatchStatus.Cancelled, replacement.Status);
        Assert.Equal(8, replacement.Revision);
        Assert.Equal(lastActivityAt, replacement.LastActivityAt);
        Assert.Equal("auth0|123", participant.UserId);
        Assert.Equal("Matias", participant.DisplayName);
        Assert.Equal(MatchStatus.Started, stored.Match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SameSnapshotForTwoWorkers_ProducesFiltersWithSameRevisionGuard()
    {
        var stored = CreateStored(MatchStatus.Waiting, revision: 9);
        var first = Render(MongoMatchCleanupOperations.BuildExpiredFilter(stored, Cutoff));
        var second = Render(MongoMatchCleanupOperations.BuildExpiredFilter(stored, Cutoff));
        var replacement = MongoMatchCleanupOperations.BuildCancellationReplacement(stored, Cutoff);

        Assert.Equal(9, first[nameof(MatchDocument.Revision)].AsInt64);
        Assert.Equal(9, second[nameof(MatchDocument.Revision)].AsInt64);
        Assert.Equal(10, replacement.Revision);
        Assert.NotEqual(replacement.Revision, second[nameof(MatchDocument.Revision)].AsInt64);
    }

    private static BsonDocument Render(FilterDefinition<MatchDocument> filter)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<MatchDocument>();
        return filter.Render(new RenderArgs<MatchDocument>(serializer, BsonSerializer.SerializerRegistry));
    }

    private static StoredMatch CreateStored(
        MatchStatus status,
        long revision,
        DateTimeOffset? lastActivityAt = null,
        string userId = "user-1",
        string? displayName = null)
    {
        var match = Match.Rehydrate(
            "match-1",
            "Cleanup match",
            "Chess",
            "owner-1",
            CreatedAt,
            lastActivityAt ?? CreatedAt,
            4,
            status,
            [new MatchParticipant(userId, CreatedAt, CreatedAt, displayName)]);
        return new StoredMatch(match, revision);
    }
}
