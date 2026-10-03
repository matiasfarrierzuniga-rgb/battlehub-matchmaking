using Microsoft.AspNetCore.SignalR;

namespace BattleHub.Matchmaking.Api.Transport.Lobby;

public sealed class LobbyHub : Hub
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
}
