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

    public static Match Rehydrate(
        string id,
        string title,
        string gameType,
        string createdBy,
        DateTimeOffset createdAt,
        DateTimeOffset lastActivityAt,
        int maxPlayers,
        MatchStatus status,
        IEnumerable<MatchParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var match = new Match(id, title, gameType, createdBy, createdAt, maxPlayers);
        var participantList = participants.ToList();

        if (participantList.Count > maxPlayers)
        {
            throw new ArgumentException("Participants cannot exceed max players.", nameof(participants));
        }

        if (participantList.Any(participant => participant is null))
        {
            throw new ArgumentException("Participants cannot contain null values.", nameof(participants));
        }

        if (participantList
            .GroupBy(participant => participant.UserId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Participant user IDs must be unique.", nameof(participants));
        }

        match.LastActivityAt = lastActivityAt;
        match.Status = status;
        match._participants.AddRange(participantList);
        return match;
    }

    public bool JoinParticipant(string userId, DateTimeOffset occurredAt)
    {
        EnsureMembershipCanChange();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (_participants.Any(participant =>
                string.Equals(participant.UserId, userId, StringComparison.Ordinal)))
        {
            return false;
        }

        if (CurrentPlayers >= MaxPlayers)
        {
            throw new MatchFullException(Id, MaxPlayers);
        }

        _participants.Add(new MatchParticipant(userId, occurredAt, occurredAt));
        UpdateLastActivity(occurredAt);
        return true;
    }

    public bool LeaveParticipant(string userId, DateTimeOffset occurredAt)
    {
        EnsureMembershipCanChange();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var participant = _participants.Find(candidate =>
            string.Equals(candidate.UserId, userId, StringComparison.Ordinal));

        if (participant is null)
        {
            return false;
        }

        _participants.Remove(participant);
        UpdateLastActivity(occurredAt);
        return true;
    }

    public bool RecordHeartbeat(string userId, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var participant = _participants.Find(candidate =>
            string.Equals(candidate.UserId, userId, StringComparison.Ordinal));

        if (participant is null)
        {
            throw new MatchParticipantNotFoundException(userId);
        }

        var participantChanged = participant.RecordHeartbeat(occurredAt);
        var activityChanged = occurredAt > LastActivityAt;
        UpdateLastActivity(occurredAt);
        return participantChanged || activityChanged;
    }

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

    private void EnsureMembershipCanChange()
    {
        if (Status != MatchStatus.Waiting)
        {
            throw new MatchMembershipChangeNotAllowedException(Status);
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
        UpdateLastActivity(occurredAt);
    }

    private void UpdateLastActivity(DateTimeOffset occurredAt)
    {
        if (occurredAt > LastActivityAt)
        {
            LastActivityAt = occurredAt;
        }
    }
}
