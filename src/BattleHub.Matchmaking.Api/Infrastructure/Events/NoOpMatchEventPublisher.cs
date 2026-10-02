using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Infrastructure.Events;

// Temporary until the Lobby Hub SignalR publisher is implemented.
public sealed class NoOpMatchEventPublisher : IMatchEventPublisher
{
    public Task PublishAsync(string eventName, Match match, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
