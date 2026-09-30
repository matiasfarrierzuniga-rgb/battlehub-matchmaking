namespace BattleHub.Matchmaking.Api.Domain.Matches;

public sealed class MatchParticipant
{
    public MatchParticipant(string userId, DateTimeOffset joinedAt, DateTimeOffset lastHeartbeatAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        UserId = userId;
        JoinedAt = joinedAt;
        LastHeartbeatAt = lastHeartbeatAt;
    }

    public string UserId { get; }

    public DateTimeOffset JoinedAt { get; }

    public DateTimeOffset LastHeartbeatAt { get; }
}
