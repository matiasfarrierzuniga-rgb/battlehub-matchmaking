using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Domain.Matches;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MongoDB.Driver;

namespace BattleHub.Matchmaking.Api.Transport;

public sealed class MatchExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            MatchNotFoundException => StatusCodes.Status404NotFound,
            MatchConcurrencyException or
            MatchFullException or
            InvalidMatchTransitionException or
            MatchMembershipChangeNotAllowedException => StatusCodes.Status409Conflict,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            ArgumentException => StatusCodes.Status400BadRequest,
            MongoException => StatusCodes.Status503ServiceUnavailable,
            _ => 0
        };

        if (statusCode == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = ReasonPhrases.GetReasonPhrase(statusCode),
                Detail = exception is MongoException
                    ? "MongoDB no está disponible."
                    : exception.Message
            }
        });
    }
}
