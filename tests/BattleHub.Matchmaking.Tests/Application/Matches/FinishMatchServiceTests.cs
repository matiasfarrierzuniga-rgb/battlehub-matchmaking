using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Configuration;
using BattleHub.Matchmaking.Api.Domain.Matches;
using Microsoft.Extensions.Options;

namespace BattleHub.Matchmaking.Tests.Application.Matches;

public class FinishMatchServiceTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Started_FinishesAndPublishesExactlyOnceAcrossRetry()
    {
        var (service, store, publisher) = Setup(Started("typing"));

        var first = await service.FinishAsync("match-1", "typing-client", default);
        var second = await service.FinishAsync("match-1", "typing-client", default);

        Assert.Equal(MatchStatus.Finished, first.Status);
        Assert.Equal(MatchStatus.Finished, second.Status);
        Assert.Equal(1, store.ReplaceCalls);
        Assert.Equal(MatchEventNames.MatchFinished, Assert.Single(publisher.Events));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConcurrentCalls_ProduceOneTransitionAndOneEvent()
    {
        var (service, store, publisher) = Setup(Started("typing"));
        store.GateFirstTwoReads = true;

        await Task.WhenAll(
            service.FinishAsync("match-1", "typing-client", default),
            service.FinishAsync("match-1", "typing-client", default));

        Assert.Equal(MatchStatus.Finished, store.Current.Status);
        Assert.Equal(1, store.SuccessfulReplaces);
        Assert.Single(publisher.Events);
    }

    [Theory]
    [InlineData(MatchStatus.Waiting)]
    [InlineData(MatchStatus.Starting)]
    [InlineData(MatchStatus.Cancelled)]
    [Trait("Category", "Unit")]
    public async Task NonStarted_IsRejected(MatchStatus status)
    {
        var (service, store, publisher) = Setup(WithStatus("typing", status));

        await Assert.ThrowsAsync<InvalidMatchTransitionException>(
            () => service.FinishAsync("match-1", "typing-client", default));

        Assert.Equal(0, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MissingMatch_ThrowsNotFound()
    {
        var (service, _, _) = Setup(null);
        await Assert.ThrowsAsync<MatchNotFoundException>(
            () => service.FinishAsync("missing", "typing-client", default));
    }

    [Theory]
    [InlineData("typing", "trivia-client")]
    [InlineData("trivia", "typing-client")]
    [Trait("Category", "Unit")]
    public async Task ClientForDifferentGameType_IsRejected(string gameType, string clientId)
    {
        var (service, store, publisher) = Setup(Started(gameType));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.FinishAsync("match-1", clientId, default));

        Assert.Equal(0, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UnauthorizedClient_IsRejectedEvenWhenAlreadyFinished()
    {
        var match = Started("typing");
        match.Finish(CreatedAt.AddMinutes(2));
        var (service, _, _) = Setup(match);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.FinishAsync("match-1", "trivia-client", default));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExpiredStartedMatch_IsRejected()
    {
        var (service, store, publisher) = Setup(Started("typing"), CreatedAt.AddHours(2));

        await Assert.ThrowsAsync<InvalidMatchTransitionException>(
            () => service.FinishAsync("match-1", "typing-client", default));

        Assert.Equal(0, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task OlderDatabaseTime_DoesNotMoveLastActivityBackward()
    {
        var match = Started("typing");
        var previousActivity = match.LastActivityAt;
        var (service, _, _) = Setup(match, CreatedAt);

        var result = await service.FinishAsync("match-1", "typing-client", default);

        Assert.Equal(previousActivity, result.LastActivityAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task FiveConflicts_ThrowsConcurrencyWithoutEvent()
    {
        var (service, store, publisher) = Setup(Started("typing"));
        store.AlwaysConflict = true;

        await Assert.ThrowsAsync<MatchConcurrencyException>(
            () => service.FinishAsync("match-1", "typing-client", default));

        Assert.Equal(5, store.ReplaceCalls);
        Assert.Empty(publisher.Events);
    }

    private static (FinishMatchService Service, FinishStore Store, FinishPublisher Publisher) Setup(
        Match? match,
        DateTimeOffset? now = null)
    {
        var store = new FinishStore(match, now ?? CreatedAt.AddMinutes(10));
        var publisher = new FinishPublisher();
        var service = new FinishMatchService(
            store,
            publisher,
            Options.Create(new GameServiceOptions
            {
                Clients = new Dictionary<string, string>
                {
                    ["typing-client"] = "typing",
                    ["trivia-client"] = "trivia"
                }
            }),
            Options.Create(new CleanupOptions { ExpirationSeconds = 7200 }));
        return (service, store, publisher);
    }

    private static Match Started(string gameType) => WithStatus(gameType, MatchStatus.Started);

    private static Match WithStatus(string gameType, MatchStatus status)
    {
        var match = new Match("match-1", "Friday", gameType, "owner-1", CreatedAt, 4);
        if (status is MatchStatus.Starting or MatchStatus.Started or MatchStatus.Cancelled)
        {
            match.RequestStart("owner-1", CreatedAt.AddMinutes(1));
        }
        if (status == MatchStatus.Started)
        {
            match.CompleteStart(CreatedAt.AddMinutes(2));
        }
        if (status == MatchStatus.Cancelled)
        {
            match.Cancel("owner-1", CreatedAt.AddMinutes(2));
        }
        return match;
    }

    private sealed class FinishStore(Match? match, DateTimeOffset now) : IMatchStore
    {
        private readonly object _lock = new();
        private StoredMatch? _stored = match is null ? null : new StoredMatch(Clone(match), 0);
        private readonly TaskCompletionSource _readGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _gatedReads;
        public bool AlwaysConflict { get; set; }
        public bool GateFirstTwoReads { get; set; }
        public int ReplaceCalls { get; private set; }
        public int SuccessfulReplaces { get; private set; }
        public Match Current => Clone(_stored!.Match);

        public Task<DateTimeOffset> GetDatabaseTimeAsync(CancellationToken cancellationToken) => Task.FromResult(now);
        public Task<StoredMatch?> GetAsync(string matchId, CancellationToken cancellationToken)
        {
            StoredMatch? snapshot;
            lock (_lock)
            {
                snapshot = _stored is null ? null : new StoredMatch(Clone(_stored.Match), _stored.Revision);
            }

            if (!GateFirstTwoReads || Interlocked.Increment(ref _gatedReads) > 2)
            {
                return Task.FromResult(snapshot);
            }

            if (Volatile.Read(ref _gatedReads) == 2)
            {
                _readGate.TrySetResult();
            }

            return AwaitReadGate(snapshot);
        }

        private async Task<StoredMatch?> AwaitReadGate(StoredMatch? snapshot)
        {
            await _readGate.Task;
            return snapshot;
        }
        public Task<IReadOnlyList<StoredMatch>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InsertAsync(Match value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryReplaceAsync(Match value, long revision, MatchStatus status, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                ReplaceCalls++;
                if (AlwaysConflict || _stored is null || _stored.Revision != revision || _stored.Match.Status != status)
                {
                    return Task.FromResult(false);
                }
                _stored = new StoredMatch(Clone(value), revision + 1);
                SuccessfulReplaces++;
                return Task.FromResult(true);
            }
        }
    }

    private sealed class FinishPublisher : IMatchEventPublisher
    {
        public List<string> Events { get; } = [];
        public Task PublishAsync(string eventName, Match match, CancellationToken cancellationToken)
        {
            lock (Events) Events.Add(eventName);
            return Task.CompletedTask;
        }
    }

    private static Match Clone(Match match) => Match.Rehydrate(
        match.Id, match.Title, match.GameType, match.CreatedBy, match.CreatedAt,
        match.LastActivityAt, match.MaxPlayers, match.Status,
        match.Participants.Select(p => new MatchParticipant(p.UserId, p.JoinedAt, p.LastHeartbeatAt)));
}
