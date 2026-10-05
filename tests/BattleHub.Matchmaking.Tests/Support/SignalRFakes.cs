using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace BattleHub.Matchmaking.Tests.Support;

internal sealed class TestHubCallerContext(string connectionId, ClaimsPrincipal? user = null) : HubCallerContext
{
    public override string ConnectionId { get; } = connectionId;
    public override string? UserIdentifier => null;
    public override ClaimsPrincipal? User { get; } = user;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() { }
}

internal sealed class RecordingGroupManager : IGroupManager
{
    public List<(string ConnectionId, string GroupName)> Added { get; } = [];
    public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

    public Task AddToGroupAsync(
        string connectionId,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(
        string connectionId,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
}

internal sealed class RecordingClientProxy : IClientProxy
{
    public List<(string Method, object?[] Arguments)> Sends { get; } = [];

    public Task SendCoreAsync(
        string method,
        object?[] args,
        CancellationToken cancellationToken = default)
    {
        Sends.Add((method, args));
        return Task.CompletedTask;
    }
}

internal sealed class RecordingHubClients : IHubClients, IHubCallerClients
{
    public RecordingClientProxy Proxy { get; } = new();
    public List<IReadOnlyList<string>> SelectedGroups { get; } = [];

    public IClientProxy All => Proxy;
    public IClientProxy Caller => Proxy;
    public IClientProxy Others => Proxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
    public IClientProxy Client(string connectionId) => Proxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;

    public IClientProxy Group(string groupName)
    {
        SelectedGroups.Add([groupName]);
        return Proxy;
    }

    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
    public IClientProxy OthersInGroup(string groupName) => Proxy;

    public IClientProxy Groups(IReadOnlyList<string> groupNames)
    {
        SelectedGroups.Add(groupNames);
        return Proxy;
    }

    public IClientProxy User(string userId) => Proxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
}

internal sealed class TestHubContext<THub>(RecordingHubClients clients, IGroupManager groups)
    : IHubContext<THub> where THub : Hub
{
    public IHubClients Clients { get; } = clients;
    public IGroupManager Groups { get; } = groups;
}
