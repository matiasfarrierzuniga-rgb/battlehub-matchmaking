using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;

public static class MatchDocumentMapper
{
    public static MatchDocument ToDocument(Match match, long revision)
    {
        ArgumentNullException.ThrowIfNull(match);

        return new MatchDocument
        {
            Id = match.Id,
            Title = match.Title,
            GameType = match.GameType,
            CreatedBy = match.CreatedBy,
            CreatedAt = match.CreatedAt.ToUniversalTime(),
            LastActivityAt = match.LastActivityAt.ToUniversalTime(),
            StartedAt = match.StartedAt?.ToUniversalTime(),
            FinishedAt = match.FinishedAt?.ToUniversalTime(),
            CancelledAt = match.CancelledAt?.ToUniversalTime(),
            MaxPlayers = match.MaxPlayers,
            Status = match.Status,
            Revision = revision,
            Participants = match.Participants
                .Select(participant => new MatchParticipantDocument
                {
                    UserId = participant.UserId,
                    DisplayName = participant.DisplayName,
                    JoinedAt = participant.JoinedAt.ToUniversalTime(),
                    LastHeartbeatAt = participant.LastHeartbeatAt.ToUniversalTime()
                })
                .ToArray()
        };
    }

    public static StoredMatch ToDomain(MatchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var participants = document.Participants.Select(participant =>
            new MatchParticipant(
                participant.UserId,
                participant.JoinedAt,
                participant.LastHeartbeatAt,
                participant.DisplayName));

        var match = Match.Rehydrate(
            document.Id,
            document.Title,
            document.GameType,
            document.CreatedBy,
            document.CreatedAt,
            document.LastActivityAt,
            document.MaxPlayers,
            document.Status,
            participants,
            document.StartedAt,
            document.FinishedAt,
            document.CancelledAt);

        return new StoredMatch(match, document.Revision);
    }
}
