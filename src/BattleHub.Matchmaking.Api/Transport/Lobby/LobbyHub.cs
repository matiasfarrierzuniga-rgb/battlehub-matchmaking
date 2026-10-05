using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Configuration;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace BattleHub.Matchmaking.Api.Transport.Lobby;

public sealed class LobbyHub(MatchService matchService) : Hub
{
    public Task JoinLobby()
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroups.Lobby);
    }

    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task JoinMatch(string matchId)
    {
        var userId = GetUserId();
        var match = await matchService.GetAsync(matchId, Context.ConnectionAborted);

        if (!match.Participants.Any(participant =>
                string.Equals(participant.UserId, userId, StringComparison.Ordinal)))
        {
            throw new UnauthorizedAccessException("Only match participants can join its SignalR group.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroups.ForMatch(matchId));
    }

    public Task LeaveMatch(string matchId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, LobbyGroups.ForMatch(matchId));
    }

    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task Heartbeat(string matchId)
    {
        var userId = GetUserId();

        await matchService.HeartbeatAsync(matchId, userId, Context.ConnectionAborted);
    }

    private string GetUserId() =>
        (Context.User is { } user ? MatchAuthorization.UserId(user) : null)
        ?? throw new UnauthorizedAccessException("An authenticated user identity is required.");
}
