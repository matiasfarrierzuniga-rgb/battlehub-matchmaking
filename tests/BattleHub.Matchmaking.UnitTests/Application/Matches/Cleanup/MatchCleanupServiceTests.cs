using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Application.Matches.Cleanup;
using BattleHub.Matchmaking.Api.Configuration;
using BattleHub.Matchmaking.Api.Domain.Matches;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BattleHub.Matchmaking.Tests.Application.Matches.Cleanup;

public class MatchCleanupServiceTests
{
    private static readonly DateTimeOffset DatabaseNow = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly CleanupOptions OptionsValue = new()
    {
        Enabled = true,
        IntervalSeconds = 30,
        HeartbeatTimeoutSeconds = 120,
        InactivitySeconds = 300,
        ExpirationSeconds = 7200,
        RetentionSeconds = 600
    };

    [Theory]
    [InlineData(MatchStatus.Finished)]
    [InlineData(MatchStatus.Cancelled)]
    [Trait("Category", "Unit")]
    public async Task ExpiredTerminalMatch_IsDeletedWithoutEvent(MatchStatus status)
    {
        var (service, store, cleanup, publisher) = Setup(Match(status, DatabaseNow.AddMinutes(-11)));

        await service.RunOnceAsync(default);

        Assert.Equal(1, cleanup.DeleteCalls);
        Assert.Empty(cleanup.CancelCalls);
        Assert.Empty(publisher.Events);
        Assert.Equal(DatabaseNow.AddSeconds(-OptionsValue.RetentionSeconds), cleanup.LastCutoff);
        Assert.Equal(1, store.DatabaseTimeCalls);
    }

    [Theory]
    [InlineData(MatchStatus.Waiting)]
    [InlineData(MatchStatus.Starting)]
    [InlineData(MatchStatus.Started)]
    [Trait("Category", "Unit")]
    public async Task ExpiredNonTerminalMatch_CancelsAndPublishesDeleted(MatchStatus status)
    {
        var match = Match(status, DatabaseNow.AddMinutes(-1), createdAt: DatabaseNow.AddHours(-3));
        var (service, _, cleanup, publisher) = Setup(match);

        await service.RunOnceAsync(default);

        Assert.Equal(["expired"], cleanup.CancelCalls);
        var published = Assert.Single(publisher.Events);
        Assert.Equal(MatchEventNames.MatchDeleted, published.Name);
        Assert.Equal(MatchStatus.Cancelled, published.Match.Status);
        Assert.Equal(DatabaseNow, published.Match.LastActivityAt);
        Assert.Equal(MatchStatus.Waiting == status ? MatchStatus.Waiting : status, match.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InactiveWaiting_CancelsAndPublishesDeleted()
    {
        var (service, _, cleanup, publisher) = Setup(
            Match(MatchStatus.Waiting, DatabaseNow.AddMinutes(-6), createdAt: DatabaseNow.AddMinutes(-30)));

        await service.RunOnceAsync(default);

        Assert.Equal(["inactive"], cleanup.CancelCalls);
        Assert.Single(publisher.Events);
        Assert.Equal(DatabaseNow.AddSeconds(-OptionsValue.InactivitySeconds), cleanup.LastCutoff);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AllParticipantsStale_CancelsAndPublishesDeleted()
    {
        var match = Match(MatchStatus.Started, DatabaseNow.AddMinutes(-1), DatabaseNow.AddMinutes(-30),
            [Participant("one", DatabaseNow.AddMinutes(-3)), Participant("two", DatabaseNow.AddMinutes(-4))]);
        var (service, _, cleanup, publisher) = Setup(match);

        await service.RunOnceAsync(default);

        Assert.Equal(["heartbeat"], cleanup.CancelCalls);
        Assert.Single(publisher.Events);
        Assert.Equal(DatabaseNow.AddSeconds(-OptionsValue.HeartbeatTimeoutSeconds), cleanup.LastCutoff);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task OneValidHeartbeat_PerformsNoOperation()
    {
        var match = Match(MatchStatus.Started, DatabaseNow.AddMinutes(-1), DatabaseNow.AddMinutes(-30),
            [Participant("stale", DatabaseNow.AddMinutes(-3)), Participant("valid", DatabaseNow.AddMinutes(-1))]);
        var (service, _, cleanup, publisher) = Setup(match);

        await service.RunOnceAsync(default);

        Assert.Empty(cleanup.CancelCalls);
        Assert.Equal(0, cleanup.DeleteCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ActiveMatch_PerformsNoOperation()
    {
        var (service, _, cleanup, publisher) = Setup(
            Match(MatchStatus.Waiting, DatabaseNow.AddMinutes(-1), DatabaseNow.AddMinutes(-30)));

        await service.RunOnceAsync(default);

        Assert.Empty(cleanup.CancelCalls);
        Assert.Equal(0, cleanup.DeleteCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task FailedAtomicMutation_DoesNotPublishOrTryAnotherCategory()
    {
        var match = Match(MatchStatus.Waiting, DatabaseNow.AddHours(-3), DatabaseNow.AddHours(-3),
            [Participant("stale", DatabaseNow.AddHours(-3))]);
        var (service, _, cleanup, publisher) = Setup(match);
        cleanup.Result = false;

        await service.RunOnceAsync(default);

        Assert.Equal(["expired"], cleanup.CancelCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EventPayload_DoesNotMoveLastActivityBackwardOrMutateSnapshot()
    {
        var futureActivity = DatabaseNow.AddMinutes(1);
        var original = Match(MatchStatus.Started, futureActivity, DatabaseNow.AddHours(-3),
            [new MatchParticipant(
                "auth0|123",
                DatabaseNow.AddMinutes(-10),
                DatabaseNow.AddMinutes(-3),
                "Matias")]);
        var (service, _, _, publisher) = Setup(original);

        await service.RunOnceAsync(default);

        var payload = Assert.Single(publisher.Events).Match;
        var publishedParticipant = Assert.Single(payload.Participants);
        var originalParticipant = Assert.Single(original.Participants);
        Assert.Equal(MatchStatus.Cancelled, payload.Status);
        Assert.Equal(futureActivity, payload.LastActivityAt);
        Assert.Equal("auth0|123", publishedParticipant.UserId);
        Assert.Equal("Matias", publishedParticipant.DisplayName);
        Assert.Equal(MatchStatus.Started, original.Status);
        Assert.Equal("auth0|123", originalParticipant.UserId);
        Assert.Equal("Matias", originalParticipant.DisplayName);
        Assert.NotSame(original, payload);
        Assert.NotSame(originalParticipant, publishedParticipant);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunOnce_UsesOneDatabaseTimeAndDerivesEveryCutoffFromIt()
    {
        var matches = new[]
        {
            Match(MatchStatus.Finished, DatabaseNow.AddMinutes(-11)),
            Match(MatchStatus.Started, DatabaseNow, DatabaseNow.AddHours(-3), id: "expired"),
            Match(MatchStatus.Waiting, DatabaseNow.AddMinutes(-6), DatabaseNow.AddMinutes(-30), id: "inactive"),
            Match(MatchStatus.Started, DatabaseNow, DatabaseNow.AddMinutes(-30),
                [Participant("stale", DatabaseNow.AddMinutes(-3))], "heartbeat")
        };
        var (service, store, cleanup, _) = Setup(matches);

        await service.RunOnceAsync(default);

        Assert.Equal(1, store.DatabaseTimeCalls);
        Assert.Equal(DatabaseNow.AddSeconds(-OptionsValue.RetentionSeconds), cleanup.Cutoffs["delete"]);
        Assert.Equal(DatabaseNow.AddSeconds(-OptionsValue.ExpirationSeconds), cleanup.Cutoffs["expired"]);
        Assert.Equal(DatabaseNow.AddSeconds(-OptionsValue.InactivitySeconds), cleanup.Cutoffs["inactive"]);
        Assert.Equal(DatabaseNow.AddSeconds(-OptionsValue.HeartbeatTimeoutSeconds), cleanup.Cutoffs["heartbeat"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TwoWorkers_SharingAtomicStore_PublishExactlyOnce()
    {
        var candidate = Match(MatchStatus.Waiting, DatabaseNow, DatabaseNow.AddHours(-3));
        var atomicStore = new FakeCleanupStore { AtomicSingleWinner = true };
        var publisher = new FakePublisher();
        var first = CreateService(new FakeMatchStore(DatabaseNow, [candidate]), atomicStore, publisher);
        var second = CreateService(new FakeMatchStore(DatabaseNow, [candidate]), atomicStore, publisher);

        await Task.WhenAll(first.RunOnceAsync(default), second.RunOnceAsync(default));

        Assert.Equal(2, atomicStore.CancelCalls.Count);
        Assert.Single(publisher.Events);
        Assert.Equal(MatchEventNames.MatchDeleted, publisher.Events[0].Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BackgroundService_WhenDisabled_DoesNotRunCleanup()
    {
        var store = new FakeMatchStore(DatabaseNow, []);
        var cleanup = new FakeCleanupStore();
        var publisher = new FakePublisher();
        var service = CreateService(store, cleanup, publisher);
        var worker = new MatchCleanupBackgroundService(
            service,
            Microsoft.Extensions.Options.Options.Create(new CleanupOptions { Enabled = false }),
            NullLogger<MatchCleanupBackgroundService>.Instance);

        await worker.StartAsync(default);
        await worker.StopAsync(default);

        Assert.Equal(0, store.DatabaseTimeCalls);
    }

    private static (MatchCleanupService, FakeMatchStore, FakeCleanupStore, FakePublisher) Setup(params Match[] matches)
    {
        var store = new FakeMatchStore(DatabaseNow, matches);
        var cleanup = new FakeCleanupStore();
        var publisher = new FakePublisher();
        return (CreateService(store, cleanup, publisher), store, cleanup, publisher);
    }

    private static MatchCleanupService CreateService(
        IMatchStore store, IMatchCleanupStore cleanup, IMatchEventPublisher publisher) =>
        new(store, cleanup, publisher, Microsoft.Extensions.Options.Options.Create(OptionsValue));

    private static Match Match(
        MatchStatus status,
        DateTimeOffset lastActivityAt,
        DateTimeOffset? createdAt = null,
        IReadOnlyCollection<MatchParticipant>? participants = null,
        string id = "match-1") =>
        BattleHub.Matchmaking.Api.Domain.Matches.Match.Rehydrate(
            id, "Cleanup", "Chess", "owner", createdAt ?? DatabaseNow.AddMinutes(-30),
            lastActivityAt, 4, status, participants ?? []);

    private static MatchParticipant Participant(string id, DateTimeOffset heartbeat) =>
        new(id, DatabaseNow.AddMinutes(-10), heartbeat);

    private sealed class FakeMatchStore(DateTimeOffset databaseNow, IEnumerable<Match> matches) : IMatchStore
    {
        private readonly IReadOnlyList<StoredMatch> _matches = matches.Select((match, revision) => new StoredMatch(match, revision)).ToList();
        public int DatabaseTimeCalls { get; private set; }
        public Task<DateTimeOffset> GetDatabaseTimeAsync(CancellationToken cancellationToken)
        {
            DatabaseTimeCalls++;
            return Task.FromResult(databaseNow);
        }
        public Task<IReadOnlyList<StoredMatch>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(_matches);
        public Task<StoredMatch?> GetAsync(string matchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InsertAsync(Match match, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryReplaceAsync(Match match, long expectedRevision, MatchStatus expectedStatus, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeCleanupStore : IMatchCleanupStore
    {
        private int _winner;
        public bool Result { get; set; } = true;
        public bool AtomicSingleWinner { get; set; }
        public int DeleteCalls { get; private set; }
        public List<string> CancelCalls { get; } = [];
        public Dictionary<string, DateTimeOffset> Cutoffs { get; } = [];
        public DateTimeOffset LastCutoff { get; private set; }

        public Task<bool> TryCancelInactiveWaitingAsync(StoredMatch stored, DateTimeOffset databaseNow, DateTimeOffset cutoff, CancellationToken cancellationToken) => Cancel("inactive", cutoff);
        public Task<bool> TryCancelWithoutValidHeartbeatAsync(StoredMatch stored, DateTimeOffset databaseNow, DateTimeOffset cutoff, CancellationToken cancellationToken) => Cancel("heartbeat", cutoff);
        public Task<bool> TryCancelExpiredAsync(StoredMatch stored, DateTimeOffset databaseNow, DateTimeOffset cutoff, CancellationToken cancellationToken) => Cancel("expired", cutoff);
        public Task<bool> TryDeleteTerminalAsync(StoredMatch stored, DateTimeOffset cutoff, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            LastCutoff = cutoff;
            Cutoffs["delete"] = cutoff;
            return Task.FromResult(Result);
        }
        private Task<bool> Cancel(string operation, DateTimeOffset cutoff)
        {
            lock (CancelCalls)
            {
                CancelCalls.Add(operation);
                LastCutoff = cutoff;
                Cutoffs[operation] = cutoff;
            }
            var result = AtomicSingleWinner ? Interlocked.CompareExchange(ref _winner, 1, 0) == 0 : Result;
            return Task.FromResult(result);
        }
    }

    private sealed class FakePublisher : IMatchEventPublisher
    {
        public List<(string Name, Match Match)> Events { get; } = [];
        public Task PublishAsync(string eventName, Match match, CancellationToken cancellationToken)
        {
            lock (Events) Events.Add((eventName, match));
            return Task.CompletedTask;
        }
    }
}
