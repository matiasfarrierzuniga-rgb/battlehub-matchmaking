namespace BattleHub.Matchmaking.Api.Domain.Matches;

public sealed class MatchParticipant
{
    public MatchParticipant(
        string userId,
        DateTimeOffset joinedAt,
        DateTimeOffset lastHeartbeatAt,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        UserId = userId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? userId : displayName;
        JoinedAt = joinedAt;
        LastHeartbeatAt = lastHeartbeatAt;
    }

    public string UserId { get; }

    public string DisplayName { get; }

    public DateTimeOffset JoinedAt { get; }

    public DateTimeOffset LastHeartbeatAt { get; private set; }

    internal bool RecordHeartbeat(DateTimeOffset occurredAt)
    {
        if (occurredAt <= LastHeartbeatAt)
        {
            return false;
        }

        LastHeartbeatAt = occurredAt;
        return true;
    }
}
