using System.Security.Claims;
using BattleHub.Matchmaking.Api.Application.Matches;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace BattleHub.Matchmaking.Api.Transport.Lobby;

public sealed class LobbyHub(MatchService matchService) : Hub
{
    public Task JoinLobby()
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroups.Lobby);
    }

    public Task JoinMatch(string matchId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroups.ForMatch(matchId));
    }

    public Task LeaveMatch(string matchId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, LobbyGroups.ForMatch(matchId));
    }

    [Authorize]
    public async Task Heartbeat(string matchId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("An authenticated user identity is required.");

        await matchService.HeartbeatAsync(matchId, userId, Context.ConnectionAborted);
    }
}
