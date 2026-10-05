using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Application.Matches;

public sealed record StoredMatch(Match Match, long Revision);
