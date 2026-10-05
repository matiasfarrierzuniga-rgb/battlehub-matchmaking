using BattleHub.Matchmaking.Api.Configuration;
using BattleHub.Matchmaking.Api.Domain.Matches;
using Microsoft.Extensions.Options;

namespace BattleHub.Matchmaking.Api.Application.Matches.Cleanup;

public sealed class MatchCleanupService(
    IMatchStore matchStore,
    IMatchCleanupStore cleanupStore,
    IMatchEventPublisher eventPublisher,
    IOptions<CleanupOptions> cleanupOptions)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var databaseNow = await matchStore.GetDatabaseTimeAsync(cancellationToken);
        var options = cleanupOptions.Value;
        var heartbeatCutoff = databaseNow.AddSeconds(-options.HeartbeatTimeoutSeconds);
        var inactivityCutoff = databaseNow.AddSeconds(-options.InactivitySeconds);
        var expirationCutoff = databaseNow.AddSeconds(-options.ExpirationSeconds);
        var retentionCutoff = databaseNow.AddSeconds(-options.RetentionSeconds);
        var matches = await matchStore.ListAsync(cancellationToken);

        foreach (var stored in matches)
        {
            var match = stored.Match;

            if (MatchCleanupEvaluator.IsTerminalRetentionExpired(match, retentionCutoff))
            {
                await cleanupStore.TryDeleteTerminalAsync(stored, retentionCutoff, cancellationToken);
                continue;
            }

            if (MatchCleanupEvaluator.IsExpired(match, expirationCutoff))
            {
                if (await cleanupStore.TryCancelExpiredAsync(
                    stored, databaseNow, expirationCutoff, cancellationToken))
                {
                    await PublishDeletedAsync(match, databaseNow, cancellationToken);
                }
                continue;
            }

            if (MatchCleanupEvaluator.IsInactiveWaiting(match, inactivityCutoff))
            {
                if (await cleanupStore.TryCancelInactiveWaitingAsync(
                    stored, databaseNow, inactivityCutoff, cancellationToken))
                {
                    await PublishDeletedAsync(match, databaseNow, cancellationToken);
                }
                continue;
            }

            if (MatchCleanupEvaluator.HasNoValidHeartbeat(match, heartbeatCutoff))
            {
                if (await cleanupStore.TryCancelWithoutValidHeartbeatAsync(
                    stored, databaseNow, heartbeatCutoff, cancellationToken))
                {
                    await PublishDeletedAsync(match, databaseNow, cancellationToken);
                }
            }
        }
    }

    private async Task PublishDeletedAsync(
        Match match,
        DateTimeOffset databaseNow,
        CancellationToken cancellationToken)
    {
        var cancelled = CreateCancelledMatch(match, databaseNow);
        await eventPublisher.PublishAsync(MatchEventNames.MatchDeleted, cancelled, cancellationToken);
    }

    private static Match CreateCancelledMatch(Match match, DateTimeOffset databaseNow)
    {
        var cancelled = Match.Rehydrate(
            match.Id,
            match.Title,
            match.GameType,
            match.CreatedBy,
            match.CreatedAt,
            match.LastActivityAt,
            match.MaxPlayers,
            match.Status,
            match.Participants.Select(participant => new MatchParticipant(
                participant.UserId,
                participant.JoinedAt,
                participant.LastHeartbeatAt)));

        cancelled.Expire(databaseNow);
        return cancelled;
    }
}
