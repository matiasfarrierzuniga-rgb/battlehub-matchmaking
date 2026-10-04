using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using BattleHub.Matchmaking.Api.Transport.Matches;
using Microsoft.AspNetCore.SignalR;

namespace BattleHub.Matchmaking.Api.Transport.Lobby;

public sealed class SignalRMatchEventPublisher(IHubContext<LobbyHub> hubContext) : IMatchEventPublisher
{
    private static readonly HashSet<string> KnownEvents =
    [
        MatchEventNames.MatchCreated,
        MatchEventNames.PlayerJoined,
        MatchEventNames.PlayerLeft,
        MatchEventNames.MatchStarting,
        MatchEventNames.MatchStarted,
        MatchEventNames.MatchFinished,
        MatchEventNames.MatchDeleted
    ];

    public Task PublishAsync(string eventName, Match match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(match);

        if (!KnownEvents.Contains(eventName))
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventName),
                eventName,
                $"The match event '{eventName}' is not supported by the SignalR publisher.");
        }

        var clients = eventName == MatchEventNames.MatchCreated
            ? hubContext.Clients.Group(LobbyGroups.Lobby)
            : hubContext.Clients.Groups(LobbyGroups.Lobby, LobbyGroups.ForMatch(match.Id));

        return clients.SendCoreAsync(eventName, [match.ToResponse()], cancellationToken);
    }
}
