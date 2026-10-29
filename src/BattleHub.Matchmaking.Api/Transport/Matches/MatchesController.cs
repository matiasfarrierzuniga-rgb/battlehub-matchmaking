using System.Security.Claims;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Configuration;
using BattleHub.Matchmaking.Api.Domain.Matches;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BattleHub.Matchmaking.Api.Transport.Matches;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(
    MatchService matchService,
    IMatchHistoryStore history) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<ActionResult<MatchResponse>> Create(
        CreateMatchRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var gameType = MatchHistory.NormalizeGameType(request.GameType ?? string.Empty)!;
        var match = await matchService.CreateAsync(
            new CreateMatchCommand(request.Title, gameType, request.MaxPlayers),
            userId,
            cancellationToken,
            User.FindFirstValue("name"));

        return CreatedAtAction(nameof(Get), new { matchId = match.Id }, match.ToResponse());
    }

    [HttpGet("history")]
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<ActionResult<MatchHistoryPageResponse>> History(
        [FromQuery] string? gameType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MatchHistory.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (Request.Query.ContainsKey("gameType") && string.IsNullOrWhiteSpace(gameType))
        {
            throw new ArgumentException("Game type is required.");
        }

        var result = await history.ListAsync(userId, gameType, page, pageSize, cancellationToken);
        return Ok(new MatchHistoryPageResponse(
            result.Items.Select(item => new MatchHistoryResponse(
                item.MatchId,
                item.GameType,
                item.Status,
                item.MaxPlayers,
                item.ParticipantCount,
                item.IsHost,
                item.CreatedAt.UtcDateTime,
                item.StartedAt?.UtcDateTime,
                item.FinishedAt?.UtcDateTime,
                item.CancelledAt?.UtcDateTime)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    [HttpGet]
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<ActionResult<MatchResponse[]>> List(
        [FromQuery] string? gameType,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        MatchStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<MatchStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid match status",
                    detail: $"'{status}' is not a valid match status.");
            }

            parsedStatus = candidate;
        }

        var matches = await matchService.ListAsync(gameType, parsedStatus, cancellationToken);
        return Ok(matches.Select(MatchResponseMapper.ToResponse).ToArray());
    }

    [HttpGet("{matchId}")]
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<ActionResult<MatchResponse>> Get(
        string matchId,
        CancellationToken cancellationToken) =>
        Ok((await matchService.GetAsync(matchId, cancellationToken)).ToResponse());

    [HttpPost("{matchId}/join")]
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<ActionResult<MatchResponse>> Join(string matchId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var match = await matchService.JoinAsync(
            matchId,
            userId,
            cancellationToken,
            User.FindFirstValue("name"));
        return Ok(match.ToResponse());
    }

    [HttpPost("{matchId}/leave")]
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<ActionResult<MatchResponse>> Leave(string matchId, CancellationToken cancellationToken) =>
        await ExecuteForUser((userId, ct) => matchService.LeaveAsync(matchId, userId, ct), cancellationToken);

    [HttpPost("{matchId}/start")]
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<ActionResult<MatchResponse>> Start(string matchId, CancellationToken cancellationToken) =>
        await ExecuteForUser((userId, ct) => matchService.StartAsync(matchId, userId, ct), cancellationToken);

    [HttpDelete("{matchId}")]
    [Authorize(Policy = MatchAuthorization.UserPolicy)]
    public async Task<ActionResult<MatchResponse>> Delete(string matchId, CancellationToken cancellationToken) =>
        await ExecuteForUser((userId, ct) => matchService.CancelAsync(matchId, userId, ct), cancellationToken);

    private async Task<ActionResult<MatchResponse>> ExecuteForUser(
        Func<string, CancellationToken, Task<Match>> action,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        return userId is null
            ? Unauthorized()
            : Ok((await action(userId, cancellationToken)).ToResponse());
    }

    private string? GetUserId() => MatchAuthorization.UserId(User);
}
