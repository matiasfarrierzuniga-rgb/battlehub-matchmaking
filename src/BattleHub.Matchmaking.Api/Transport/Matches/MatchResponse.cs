namespace BattleHub.Matchmaking.Api.Transport.Matches;

public sealed record MatchResponse(
    string Id,
    string Title,
    string GameType,
    string CreatedBy,
    DateTime CreatedAt,
    int CurrentPlayers,
    int MaxPlayers,
    string Status,
    MatchParticipantResponse[] Participants);

public sealed record MatchParticipantResponse(
    string UserId,
    string DisplayName);
