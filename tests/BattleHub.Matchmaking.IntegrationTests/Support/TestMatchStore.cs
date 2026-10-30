using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Tests.Support;

internal sealed class TestMatchStore : IMatchStore
{
    private readonly Dictionary<string, StoredMatch> _matches = [];

    public DateTimeOffset Now { get; set; } = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    public Task<DateTimeOffset> GetDatabaseTimeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Now);

    public Task<StoredMatch?> GetAsync(string matchId, CancellationToken cancellationToken) =>
        Task.FromResult(_matches.GetValueOrDefault(matchId));

    public Task<IReadOnlyList<StoredMatch>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredMatch>>(_matches.Values.ToArray());

    public Task InsertAsync(Match match, CancellationToken cancellationToken)
    {
        _matches.Add(match.Id, new StoredMatch(match, 0));
        return Task.CompletedTask;
    }

    public Task<bool> TryReplaceAsync(
        Match match,
        long expectedRevision,
        MatchStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!_matches.TryGetValue(match.Id, out var stored)
            || stored.Revision != expectedRevision)
        {
            return Task.FromResult(false);
        }

        _matches[match.Id] = new StoredMatch(match, expectedRevision + 1);
        return Task.FromResult(true);
    }

    public void Add(Match match) => _matches.Add(match.Id, new StoredMatch(match, 0));
}

internal sealed class TestEventPublisher : IMatchEventPublisher
{
    public Task PublishAsync(string eventName, Match match, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
