using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Application.Matches;

public class MatchServiceTests
{
    private const string MatchId = "match-1";
    private const string OwnerId = "owner-1";
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DatabaseNow = CreatedAt.AddMinutes(5);

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Create_UsesDatabaseTimeAndAutomaticallyJoinsOwner()
    {
        var (service, store, publisher) = CreateService();

        var match = await service.CreateAsync(
            new CreateMatchCommand("Friday match", "Chess", 4), OwnerId, default);

        Assert.Equal(DatabaseNow, match.CreatedAt);
        Assert.Equal(DatabaseNow, match.LastActivityAt);
        Assert.Equal(1, match.CurrentPlayers);
        var owner = Assert.Single(match.Participants);
        Assert.Equal(OwnerId, owner.UserId);
        Assert.Equal(DatabaseNow, owner.JoinedAt);
        Assert.Same(match, Assert.Single(store.Inserts));
        Assert.Equal(MatchEventNames.MatchCreated, Assert.Single(publisher.Events).Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Create_PublishesOnlyAfterSuccessfulInsert()
    {
        var trace = new List<string>();
        var store = new FakeMatchStore(DatabaseNow, trace) { ThrowOnInsert = true };
        var publisher = new FakeEventPublisher(trace);
        var service = new MatchService(store, publisher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new CreateMatchCommand("Friday match", "Chess", 4), OwnerId, default));

        Assert.Equal(["insert"], trace);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Create_OrdersInsertBeforeEvent()
    {
        var trace = new List<string>();
        var store = new FakeMatchStore(DatabaseNow, trace);
        var publisher = new FakeEventPublisher(trace);
        var service = new MatchService(store, publisher);

        await service.CreateAsync(new("Friday match", "Chess", 4), OwnerId, default);

        Assert.Equal(["insert", $"event:{MatchEventNames.MatchCreated}"], trace);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Get_ReturnsExistingMatch()
    {
        var (service, store, _) = CreateService();
        store.Set(CreateMatch());

        var result = await service.GetAsync(MatchId, default);

        Assert.Equal(MatchId, result.Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Get_WhenMissing_ThrowsMatchNotFound()
    {
        var (service, _, _) = CreateService();

        var error = await Assert.ThrowsAsync<MatchNotFoundException>(
            () => service.GetAsync(MatchId, default));

        Assert.Equal(MatchId, error.MatchId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task List_WithoutFilters_ReturnsEveryMatch()
    {
        var (service, store, _) = CreateService();
        store.Set(CreateMatch(MatchId, "Chess"));
        store.Set(CreateMatch("match-2", "Go"));

        var result = await service.ListAsync(cancellationToken: default);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task List_FiltersGameType()
    {
        var (service, store, _) = CreateService();
        store.Set(CreateMatch(MatchId, "Chess"));
        store.Set(CreateMatch("match-2", "Go"));

        var result = await service.ListAsync(gameType: "Chess", cancellationToken: default);

        Assert.Equal(MatchId, Assert.Single(result).Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task List_FiltersStatus()
    {
        var (service, store, _) = CreateService();
        store.Set(CreateMatch(MatchId, "Chess"));
        store.Set(CreateMatch("match-2", "Chess", MatchStatus.Cancelled));

        var result = await service.ListAsync(status: MatchStatus.Cancelled, cancellationToken: default);

        Assert.Equal("match-2", Assert.Single(result).Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task List_CombinesFilters()
    {
        var (service, store, _) = CreateService();
        store.Set(CreateMatch(MatchId, "Chess", MatchStatus.Cancelled));
        store.Set(CreateMatch("match-2", "Go", MatchStatus.Cancelled));
        store.Set(CreateMatch("match-3", "Chess"));

        var result = await service.ListAsync("Chess", MatchStatus.Cancelled, default);

        Assert.Equal(MatchId, Assert.Single(result).Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Join_PersistsThenPublishesAndUsesDatabaseTime()
    {
        var trace = new List<string>();
        var (service, store, publisher) = CreateService(trace);
        store.Set(CreateMatch());

        var result = await service.JoinAsync(MatchId, "user-2", default);

        Assert.Equal(1, store.ReplaceCalls);
        Assert.Equal(DatabaseNow, Assert.Single(result.Participants).JoinedAt);
        Assert.Equal(MatchEventNames.PlayerJoined, Assert.Single(publisher.Events).Name);
        Assert.Equal(["replace", $"event:{MatchEventNames.PlayerJoined}"], trace);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Join_Duplicate_DoesNotPersistOrPublish()
    {
        var match = CreateMatch();
        match.JoinParticipant("user-2", CreatedAt);
        var (service, store, publisher) = CreateService();
        store.Set(match);

        var result = await service.JoinAsync(MatchId, "user-2", default);

        Assert.Equal(1, result.CurrentPlayers);
        Assert.Equal(0, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Join_AfterConflict_RetriesAndPublishesOnce()
    {
        var (service, store, publisher) = CreateService();
        store.Set(CreateMatch());
        store.ReplaceResults.Enqueue(false);
        store.ReplaceResults.Enqueue(true);

        await service.JoinAsync(MatchId, "user-2", default);

        Assert.Equal(2, store.ReplaceCalls);
        Assert.Single(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Join_FailedReplace_DoesNotPublish()
    {
        var (service, store, publisher) = CreateService();
        store.Set(CreateMatch());
        EnqueueConflicts(store);

        await Assert.ThrowsAsync<MatchConcurrencyException>(
            () => service.JoinAsync(MatchId, "user-2", default));

        Assert.Equal(5, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Leave_PersistsThenPublishes()
    {
        var trace = new List<string>();
        var match = CreateMatch();
        match.JoinParticipant("user-2", CreatedAt);
        var (service, store, publisher) = CreateService(trace);
        store.Set(match);

        var result = await service.LeaveAsync(MatchId, "user-2", default);

        Assert.Empty(result.Participants);
        Assert.Equal(1, store.ReplaceCalls);
        Assert.Equal(MatchEventNames.PlayerLeft, Assert.Single(publisher.Events).Name);
        Assert.Equal(["replace", $"event:{MatchEventNames.PlayerLeft}"], trace);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Leave_WhenAbsent_DoesNotPersistOrPublish()
    {
        var (service, store, publisher) = CreateService();
        store.Set(CreateMatch());

        await service.LeaveAsync(MatchId, "user-2", default);

        Assert.Equal(0, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Leave_AfterConflict_Retries()
    {
        var match = CreateMatch();
        match.JoinParticipant("user-2", CreatedAt);
        var (service, store, publisher) = CreateService();
        store.Set(match);
        store.ReplaceResults.Enqueue(false);
        store.ReplaceResults.Enqueue(true);

        await service.LeaveAsync(MatchId, "user-2", default);

        Assert.Equal(2, store.ReplaceCalls);
        Assert.Single(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Start_ByOwner_PersistsBothTransitionsAndPublishesInOrder()
    {
        var trace = new List<string>();
        var (service, store, publisher) = CreateService(trace);
        store.Set(CreateMatch());

        var result = await service.StartAsync(MatchId, OwnerId, default);

        Assert.Equal(MatchStatus.Started, result.Status);
        Assert.Equal(2, store.ReplaceCalls);
        Assert.Equal(
            [MatchEventNames.MatchStarting, MatchEventNames.MatchStarted],
            publisher.Events.Select(item => item.Name));
        Assert.Equal(
            ["replace", $"event:{MatchEventNames.MatchStarting}", "replace", $"event:{MatchEventNames.MatchStarted}"],
            trace);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Start_ByNonOwner_DoesNotPersistOrPublish()
    {
        var (service, store, publisher) = CreateService();
        store.Set(CreateMatch());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.StartAsync(MatchId, "other-user", default));

        Assert.Equal(0, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Start_WhenAnotherInstanceCompletesPhaseB_ReturnsStartedWithoutDuplicateEvent()
    {
        var (service, store, publisher) = CreateService();
        store.Set(CreateMatch());
        store.ReplaceResults.Enqueue(true);
        store.ReplaceResults.Enqueue(false);
        store.AfterReplace = (fake, call, succeeded) =>
        {
            if (call == 2 && !succeeded)
            {
                var competing = fake.ReadCurrent(MatchId);
                competing.CompleteStart(DatabaseNow.AddSeconds(1));
                fake.Set(competing, revision: 2);
            }
        };

        var result = await service.StartAsync(MatchId, OwnerId, default);

        Assert.Equal(MatchStatus.Started, result.Status);
        Assert.Equal([MatchEventNames.MatchStarting], publisher.Events.Select(item => item.Name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Cancel_ByOwner_PersistsCancelledThenPublishesDeleted()
    {
        var trace = new List<string>();
        var (service, store, publisher) = CreateService(trace);
        store.Set(CreateMatch());

        var result = await service.CancelAsync(MatchId, OwnerId, default);

        Assert.Equal(MatchStatus.Cancelled, result.Status);
        Assert.Equal(MatchEventNames.MatchDeleted, Assert.Single(publisher.Events).Name);
        Assert.Equal(["replace", $"event:{MatchEventNames.MatchDeleted}"], trace);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Cancel_ByNonOwner_DoesNotPersistOrPublish()
    {
        var (service, store, publisher) = CreateService();
        store.Set(CreateMatch());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.CancelAsync(MatchId, "other-user", default));

        Assert.Equal(0, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Cancel_AfterConflict_RetriesAndPublishesOnlyForSuccessfulReplace()
    {
        var (service, store, publisher) = CreateService();
        store.Set(CreateMatch());
        store.ReplaceResults.Enqueue(false);
        store.ReplaceResults.Enqueue(true);

        await service.CancelAsync(MatchId, OwnerId, default);

        Assert.Equal(2, store.ReplaceCalls);
        Assert.Single(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Cancel_WhenEveryReplaceConflicts_ThrowsWithoutPublishing()
    {
        var (service, store, publisher) = CreateService();
        store.Set(CreateMatch());
        EnqueueConflicts(store);

        await Assert.ThrowsAsync<MatchConcurrencyException>(
            () => service.CancelAsync(MatchId, OwnerId, default));

        Assert.Empty(publisher.Events);
    }

    private static (MatchService Service, FakeMatchStore Store, FakeEventPublisher Publisher)
        CreateService(List<string>? trace = null)
    {
        var store = new FakeMatchStore(DatabaseNow, trace);
        var publisher = new FakeEventPublisher(trace);
        return (new MatchService(store, publisher), store, publisher);
    }

    private static Match CreateMatch(
        string id = MatchId,
        string gameType = "Chess",
        MatchStatus status = MatchStatus.Waiting)
    {
        var match = new Match(id, "Friday match", gameType, OwnerId, CreatedAt, 4);
        if (status == MatchStatus.Cancelled)
        {
            match.Cancel(OwnerId, CreatedAt.AddMinutes(1));
        }

        return match;
    }

    private static void EnqueueConflicts(FakeMatchStore store)
    {
        for (var index = 0; index < 5; index++)
        {
            store.ReplaceResults.Enqueue(false);
        }
    }

    private sealed class FakeMatchStore(DateTimeOffset databaseNow, List<string>? trace = null) : IMatchStore
    {
        private readonly Dictionary<string, StoredMatch> _matches = [];

        public Queue<bool> ReplaceResults { get; } = new();
        public List<Match> Inserts { get; } = [];
        public List<Match> Replacements { get; } = [];
        public bool ThrowOnInsert { get; init; }
        public int ReplaceCalls { get; private set; }
        public Action<FakeMatchStore, int, bool>? AfterReplace { get; set; }

        public Task<DateTimeOffset> GetDatabaseTimeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(databaseNow);

        public Task<StoredMatch?> GetAsync(string matchId, CancellationToken cancellationToken) =>
            Task.FromResult(_matches.TryGetValue(matchId, out var stored)
                ? new StoredMatch(Clone(stored.Match), stored.Revision)
                : null);

        public Task<IReadOnlyList<StoredMatch>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredMatch>>(
                _matches.Values.Select(item => new StoredMatch(Clone(item.Match), item.Revision)).ToArray());

        public Task InsertAsync(Match match, CancellationToken cancellationToken)
        {
            trace?.Add("insert");
            if (ThrowOnInsert)
            {
                throw new InvalidOperationException("Insert failed.");
            }

            Inserts.Add(match);
            _matches.Add(match.Id, new StoredMatch(Clone(match), 0));
            return Task.CompletedTask;
        }

        public Task<bool> TryReplaceAsync(
            Match match,
            long expectedRevision,
            MatchStatus expectedStatus,
            CancellationToken cancellationToken)
        {
            ReplaceCalls++;
            Replacements.Add(Clone(match));
            trace?.Add("replace");
            var configuredResult = ReplaceResults.Count == 0 || ReplaceResults.Dequeue();
            var matchesExpectation = _matches.TryGetValue(match.Id, out var current)
                && current.Revision == expectedRevision
                && current.Match.Status == expectedStatus;
            var succeeded = configuredResult && matchesExpectation;

            if (succeeded)
            {
                _matches[match.Id] = new StoredMatch(Clone(match), expectedRevision + 1);
            }

            AfterReplace?.Invoke(this, ReplaceCalls, succeeded);
            return Task.FromResult(succeeded);
        }

        public void Set(Match match, long revision = 0) =>
            _matches[match.Id] = new StoredMatch(Clone(match), revision);

        public Match ReadCurrent(string matchId) => Clone(_matches[matchId].Match);

        private static Match Clone(Match match) => Match.Rehydrate(
            match.Id,
            match.Title,
            match.GameType,
            match.CreatedBy,
            match.CreatedAt,
            match.LastActivityAt,
            match.MaxPlayers,
            match.Status,
            match.Participants.Select(participant => new MatchParticipant(
                participant.UserId, participant.JoinedAt, participant.LastHeartbeatAt)));
    }

    private sealed class FakeEventPublisher(List<string>? trace = null) : IMatchEventPublisher
    {
        public List<(string Name, Match Match)> Events { get; } = [];

        public Task PublishAsync(string eventName, Match match, CancellationToken cancellationToken)
        {
            trace?.Add($"event:{eventName}");
            Events.Add((eventName, match));
            return Task.CompletedTask;
        }
    }
}
