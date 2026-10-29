using BattleHub.Matchmaking.Api.Configuration;
using BattleHub.Matchmaking.Api.Domain.Matches;
using Microsoft.Extensions.Options;

namespace BattleHub.Matchmaking.Api.Application.Matches;

public sealed class FinishMatchService(
    IMatchStore store,
    IMatchEventPublisher eventPublisher,
    IMatchHistoryStore history,
    IOptions<CleanupOptions> cleanup)
{
    private const int MaxConcurrencyAttempts = 5;

    public async Task<Match> FinishAsync(
        string matchId,
        string userId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
        {
            var stored = await store.GetAsync(matchId, cancellationToken)
                ?? throw new MatchNotFoundException(matchId);
            var match = stored.Match;

            if (!match.Participants.Any(participant =>
                    string.Equals(participant.UserId, userId, StringComparison.Ordinal)))
            {
                throw new UnauthorizedAccessException("Only a match participant can finish it.");
            }

            if (match.Status == MatchStatus.Finished)
            {
                await history.RecordAsync(match, cancellationToken);
                return match;
            }

            var databaseNow = await store.GetDatabaseTimeAsync(cancellationToken);
            if (match.Status != MatchStatus.Started)
            {
                throw new InvalidMatchTransitionException(match.Status, MatchStatus.Finished);
            }

            if (databaseNow >= match.CreatedAt.AddSeconds(cleanup.Value.ExpirationSeconds))
            {
                throw new InvalidMatchTransitionException(match.Status, MatchStatus.Finished);
            }

            match.Finish(databaseNow);
            if (await store.TryReplaceAsync(
                    match, stored.Revision, MatchStatus.Started, cancellationToken))
            {
                await eventPublisher.PublishAsync(
                    MatchEventNames.MatchFinished, match, cancellationToken);
                await history.RecordAsync(match, cancellationToken);
                return match;
            }
        }

        throw new MatchConcurrencyException(matchId);
    }
}
