using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace BattleHub.Matchmaking.Tests.Support;

internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "TestAuth";
    public const string NameIdentifierHeader = "X-Test-NameIdentifier";
    public const string SubHeader = "X-Test-Sub";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>();
        AddClaim(NameIdentifierHeader, ClaimTypes.NameIdentifier, claims);
        AddClaim(SubHeader, "sub", claims);

        if (claims.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, AuthenticationScheme)));
    }

    private void AddClaim(string header, string claimType, ICollection<Claim> claims)
    {
        if (Request.Headers.TryGetValue(header, out var values)
            && !string.IsNullOrWhiteSpace(values.ToString()))
        {
            claims.Add(new Claim(claimType, values.ToString()));
        }
    }
}
