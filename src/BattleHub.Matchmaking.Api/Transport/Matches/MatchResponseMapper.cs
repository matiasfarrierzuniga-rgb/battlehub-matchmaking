using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Transport.Matches;

public static class MatchResponseMapper
{
    public static MatchResponse ToResponse(this Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return new MatchResponse(
            match.Id,
            match.Title,
            NormalizeGameType(match.GameType),
            match.CreatedBy,
            match.CreatedAt.UtcDateTime,
            match.CurrentPlayers,
            match.MaxPlayers,
            match.Status.ToString(),
            match.Participants
                .Select(participant => new MatchParticipantResponse(
                    participant.UserId,
                    participant.DisplayName))
                .ToArray());
    }

    private static string NormalizeGameType(string gameType) => gameType.ToLowerInvariant() switch
    {
        "typing" => "typing",
        "trivia" => "trivia",
        "memory" => "memory",
        _ => gameType
    };
}
