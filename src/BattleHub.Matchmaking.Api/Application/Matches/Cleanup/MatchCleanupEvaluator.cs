using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Application.Matches.Cleanup;

public static class MatchCleanupEvaluator
{
    public static bool IsInactiveWaiting(Match match, DateTimeOffset inactivityCutoff)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Status == MatchStatus.Waiting && match.LastActivityAt <= inactivityCutoff;
    }

    public static bool HasNoValidHeartbeat(Match match, DateTimeOffset heartbeatCutoff)
    {
        ArgumentNullException.ThrowIfNull(match);
        return IsNonTerminal(match.Status)
            && match.Participants.Count > 0
            && !match.Participants.Any(participant => participant.LastHeartbeatAt > heartbeatCutoff);
    }

    public static bool IsExpired(Match match, DateTimeOffset expirationCutoff)
    {
        ArgumentNullException.ThrowIfNull(match);
        return IsNonTerminal(match.Status) && match.CreatedAt <= expirationCutoff;
    }

    public static bool IsTerminalRetentionExpired(Match match, DateTimeOffset retentionCutoff)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Status is MatchStatus.Finished or MatchStatus.Cancelled
            && match.LastActivityAt <= retentionCutoff;
    }

    private static bool IsNonTerminal(MatchStatus status) =>
        status is MatchStatus.Waiting or MatchStatus.Starting or MatchStatus.Started;
}
