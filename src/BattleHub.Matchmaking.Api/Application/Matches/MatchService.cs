using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Application.Matches;

public sealed class MatchService(IMatchStore store, IMatchEventPublisher eventPublisher)
{
    private const int MaxConcurrencyAttempts = 5;

    public async Task<Match> CreateAsync(
        CreateMatchCommand command,
        string createdBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        var databaseNow = await store.GetDatabaseTimeAsync(cancellationToken);
        var match = new Match(
            Guid.NewGuid().ToString(),
            command.Title,
            command.GameType,
            createdBy,
            databaseNow,
            command.MaxPlayers);

        match.JoinParticipant(createdBy, databaseNow);
        await store.InsertAsync(match, cancellationToken);
        await eventPublisher.PublishAsync(MatchEventNames.MatchCreated, match, cancellationToken);
        return match;
    }

    public async Task<Match> GetAsync(string matchId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
        return (await store.GetAsync(matchId, cancellationToken))?.Match
            ?? throw new MatchNotFoundException(matchId);
    }

    public async Task<IReadOnlyList<Match>> ListAsync(
        string? gameType = null,
        MatchStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var storedMatches = await store.ListAsync(cancellationToken);
        return storedMatches
            .Select(stored => stored.Match)
            .Where(match => gameType is null || match.GameType == gameType)
            .Where(match => status is null || match.Status == status)
            .ToArray();
    }

    public Task<Match> JoinAsync(string matchId, string userId, CancellationToken cancellationToken) =>
        UpdateMembershipAsync(matchId, userId, join: true, cancellationToken);

    public Task<Match> LeaveAsync(string matchId, string userId, CancellationToken cancellationToken) =>
        UpdateMembershipAsync(matchId, userId, join: false, cancellationToken);

    public async Task<Match> HeartbeatAsync(
        string matchId,
        string userId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
        {
            var stored = await GetStoredAsync(matchId, cancellationToken);
            var match = stored.Match;
            var expectedStatus = match.Status;
            var databaseNow = await store.GetDatabaseTimeAsync(cancellationToken);

            if (!match.RecordHeartbeat(userId, databaseNow))
            {
                return match;
            }

            if (await store.TryReplaceAsync(
                    match, stored.Revision, expectedStatus, cancellationToken))
            {
                return match;
            }
        }

        throw new MatchConcurrencyException(matchId);
    }

    public async Task<Match> StartAsync(
        string matchId,
        string actorId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var startingMatch = await PersistStartingAsync(matchId, actorId, cancellationToken);
        if (startingMatch.Status == MatchStatus.Started)
        {
            return startingMatch;
        }

        for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
        {
            var stored = await GetStoredAsync(matchId, cancellationToken);
            var match = stored.Match;

            EnsureOwner(match, actorId);
            if (match.Status == MatchStatus.Started)
            {
                return match;
            }

            var expectedStatus = match.Status;
            var databaseNow = await store.GetDatabaseTimeAsync(cancellationToken);
            match.CompleteStart(databaseNow);

            if (await store.TryReplaceAsync(
                    match, stored.Revision, expectedStatus, cancellationToken))
            {
                await eventPublisher.PublishAsync(
                    MatchEventNames.MatchStarted, match, cancellationToken);
                return match;
            }
        }

        throw new MatchConcurrencyException(matchId);
    }

    public async Task<Match> CancelAsync(
        string matchId,
        string actorId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
        {
            var stored = await GetStoredAsync(matchId, cancellationToken);
            var match = stored.Match;
            var expectedStatus = match.Status;
            var databaseNow = await store.GetDatabaseTimeAsync(cancellationToken);
            match.Cancel(actorId, databaseNow);

            if (await store.TryReplaceAsync(
                    match, stored.Revision, expectedStatus, cancellationToken))
            {
                await eventPublisher.PublishAsync(
                    MatchEventNames.MatchDeleted, match, cancellationToken);
                return match;
            }
        }

        throw new MatchConcurrencyException(matchId);
    }

    private async Task<Match> UpdateMembershipAsync(
        string matchId,
        string userId,
        bool join,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
        {
            var stored = await GetStoredAsync(matchId, cancellationToken);
            var match = stored.Match;
            var expectedStatus = match.Status;
            var databaseNow = await store.GetDatabaseTimeAsync(cancellationToken);
            var changed = join
                ? match.JoinParticipant(userId, databaseNow)
                : match.LeaveParticipant(userId, databaseNow);

            if (!changed)
            {
                return match;
            }

            if (await store.TryReplaceAsync(
                    match, stored.Revision, expectedStatus, cancellationToken))
            {
                await eventPublisher.PublishAsync(
                    join ? MatchEventNames.PlayerJoined : MatchEventNames.PlayerLeft,
                    match,
                    cancellationToken);
                return match;
            }
        }

        throw new MatchConcurrencyException(matchId);
    }

    private async Task<Match> PersistStartingAsync(
        string matchId,
        string actorId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
        {
            var stored = await GetStoredAsync(matchId, cancellationToken);
            var match = stored.Match;

            EnsureOwner(match, actorId);
            if (match.Status is MatchStatus.Starting or MatchStatus.Started)
            {
                return match;
            }

            var expectedStatus = match.Status;
            var databaseNow = await store.GetDatabaseTimeAsync(cancellationToken);
            match.RequestStart(actorId, databaseNow);

            if (await store.TryReplaceAsync(
                    match, stored.Revision, expectedStatus, cancellationToken))
            {
                await eventPublisher.PublishAsync(
                    MatchEventNames.MatchStarting, match, cancellationToken);
                return match;
            }
        }

        throw new MatchConcurrencyException(matchId);
    }

    private async Task<StoredMatch> GetStoredAsync(
        string matchId,
        CancellationToken cancellationToken) =>
        await store.GetAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

    private static void EnsureOwner(Match match, string actorId)
    {
        if (!string.Equals(match.CreatedBy, actorId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the match owner can perform this operation.");
        }
    }
}
