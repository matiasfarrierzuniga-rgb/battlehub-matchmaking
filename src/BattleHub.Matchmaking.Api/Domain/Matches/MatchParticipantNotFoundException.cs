namespace BattleHub.Matchmaking.Api.Domain.Matches;

public sealed class MatchParticipantNotFoundException(string userId)
    : InvalidOperationException($"User '{userId}' is not a current participant of the match.")
{
    public string UserId { get; } = userId;
}
