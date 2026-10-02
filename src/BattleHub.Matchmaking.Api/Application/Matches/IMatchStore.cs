using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Application.Matches;

public interface IMatchStore
{
    Task<DateTimeOffset> GetDatabaseTimeAsync(CancellationToken cancellationToken);

    Task<StoredMatch?> GetAsync(string matchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredMatch>> ListAsync(CancellationToken cancellationToken);

    Task InsertAsync(Match match, CancellationToken cancellationToken);

    Task<bool> TryReplaceAsync(
        Match match,
        long expectedRevision,
        MatchStatus expectedStatus,
        CancellationToken cancellationToken);
}
