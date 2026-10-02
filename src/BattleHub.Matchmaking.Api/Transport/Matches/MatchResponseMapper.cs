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
            match.GameType,
            match.CreatedBy,
            match.CreatedAt.UtcDateTime,
            match.CurrentPlayers,
            match.MaxPlayers,
            match.Status.ToString());
    }
}
