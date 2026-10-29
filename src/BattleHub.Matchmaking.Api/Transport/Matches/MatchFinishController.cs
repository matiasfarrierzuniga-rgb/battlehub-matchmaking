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
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<IActionResult> Finish(string matchId, CancellationToken cancellationToken)
    {
        var userId = MatchAuthorization.UserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        await finishMatchService.FinishAsync(matchId, userId, cancellationToken);
        return NoContent();
    }
}
