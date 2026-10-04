using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BattleHub.Matchmaking.Api.Transport.Matches;

[ApiController]
[Route("api/matches")]
public sealed class MatchFinishController(FinishMatchService finishMatchService) : ControllerBase
{
    [HttpPost("{matchId}/finish")]
    [Authorize(Policy = MatchAuthorization.FinishPolicy)]
    public async Task<IActionResult> Finish(string matchId, CancellationToken cancellationToken)
    {
        var clientId = MatchAuthorization.MachineClientId(User);
        if (clientId is null)
        {
            return Forbid();
        }

        await finishMatchService.FinishAsync(matchId, clientId, cancellationToken);
        return NoContent();
    }
}
