using System.Collections.ObjectModel;

namespace BattleHub.Matchmaking.Api.Domain.Matches;

public sealed class Match
{
    private readonly List<MatchParticipant> _participants = [];

    public Match(
        string id,
        string title,
        string gameType,
        string createdBy,
        DateTimeOffset createdAt,
        int maxPlayers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameType);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        if (maxPlayers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Max players must be greater than zero.");
        }

        Id = id;
        Title = title;
        GameType = gameType;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        LastActivityAt = createdAt;
        MaxPlayers = maxPlayers;
        Status = MatchStatus.Waiting;
        Participants = new ReadOnlyCollection<MatchParticipant>(_participants);
    }

    public string Id { get; }

    public string Title { get; }

    public string GameType { get; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public int MaxPlayers { get; }

    public MatchStatus Status { get; private set; }

    public IReadOnlyCollection<MatchParticipant> Participants { get; }

    public int CurrentPlayers => _participants.Count;

    public void RequestStart(string actorId, DateTimeOffset occurredAt)
    {
        EnsureOwner(actorId);
        Transition(MatchStatus.Waiting, MatchStatus.Starting, occurredAt);
    }

    public void CompleteStart(DateTimeOffset occurredAt)
    {
        Transition(MatchStatus.Starting, MatchStatus.Started, occurredAt);
    }

    public void Finish(DateTimeOffset occurredAt)
    {
        Transition(MatchStatus.Started, MatchStatus.Finished, occurredAt);
    }

    public void Cancel(string actorId, DateTimeOffset occurredAt)
    {
        EnsureOwner(actorId);

        if (Status is not (MatchStatus.Waiting or MatchStatus.Starting))
        {
            throw new InvalidMatchTransitionException(Status, MatchStatus.Cancelled);
        }

        ApplyTransition(MatchStatus.Cancelled, occurredAt);
    }

    public void Expire(DateTimeOffset occurredAt)
    {
        if (Status is not (MatchStatus.Waiting or MatchStatus.Starting or MatchStatus.Started))
        {
            throw new InvalidMatchTransitionException(Status, MatchStatus.Cancelled);
        }

        ApplyTransition(MatchStatus.Cancelled, occurredAt);
    }

    private void EnsureOwner(string actorId)
    {
        if (!string.Equals(actorId, CreatedBy, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the match owner can perform this operation.");
        }
    }

    private void Transition(MatchStatus expectedStatus, MatchStatus targetStatus, DateTimeOffset occurredAt)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidMatchTransitionException(Status, targetStatus);
        }

        ApplyTransition(targetStatus, occurredAt);
    }

    private void ApplyTransition(MatchStatus targetStatus, DateTimeOffset occurredAt)
    {
        Status = targetStatus;
        LastActivityAt = occurredAt;
    }
}
