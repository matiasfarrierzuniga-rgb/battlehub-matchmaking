using BattleHub.Matchmaking.Api.Domain.Matches;

namespace BattleHub.Matchmaking.Api.Application.Matches;

public interface IMatchEventPublisher
{
    Task PublishAsync(string eventName, Match match, CancellationToken cancellationToken);
}
