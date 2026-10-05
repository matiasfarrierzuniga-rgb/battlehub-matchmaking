namespace BattleHub.Matchmaking.Api.Application.Matches.Cleanup;

public interface IMatchCleanupStore
{
    Task<bool> TryCancelInactiveWaitingAsync(
        StoredMatch stored,
        DateTimeOffset databaseNow,
        DateTimeOffset inactivityCutoff,
        CancellationToken cancellationToken);

    Task<bool> TryCancelWithoutValidHeartbeatAsync(
        StoredMatch stored,
        DateTimeOffset databaseNow,
        DateTimeOffset heartbeatCutoff,
        CancellationToken cancellationToken);

    Task<bool> TryCancelExpiredAsync(
        StoredMatch stored,
        DateTimeOffset databaseNow,
        DateTimeOffset expirationCutoff,
        CancellationToken cancellationToken);

    Task<bool> TryDeleteTerminalAsync(
        StoredMatch stored,
        DateTimeOffset retentionCutoff,
        CancellationToken cancellationToken);
}
