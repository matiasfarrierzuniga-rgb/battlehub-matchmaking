using System.Security.Claims;
using BattleHub.Matchmaking.Api.Configuration;

namespace BattleHub.Matchmaking.Tests.Configuration;

public class MatchAuthorizationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void NormalUser_IsUserAndNotMachine()
    {
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, "user-1"));

        Assert.True(MatchAuthorization.IsUser(principal));
        Assert.Null(MatchAuthorization.MachineClientId(principal));
        Assert.False(MatchAuthorization.CanFinish(principal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidMachine_WithPermission_CanFinish()
    {
        var principal = Machine("typing-client", "matches.read matches.finish");

        Assert.Equal("typing-client", MatchAuthorization.MachineClientId(principal));
        Assert.True(MatchAuthorization.CanFinish(principal));
        Assert.False(MatchAuthorization.IsUser(principal));
    }

    [Theory]
    [InlineData("matches.create", "typing-client", "client-credentials")]
    [InlineData("matches.finish.admin", "typing-client", "client-credentials")]
    [InlineData("matches.finish", "other-client", "client-credentials")]
    [InlineData("matches.finish", "typing-client", null)]
    [Trait("Category", "Unit")]
    public void InvalidMachine_CannotFinish(string scope, string azp, string? gty)
    {
        var principal = Principal(
            new Claim("sub", "typing-client@clients"),
            new Claim("azp", azp),
            new Claim("scope", scope),
            new Claim("gty", gty ?? string.Empty));

        Assert.False(MatchAuthorization.CanFinish(principal));

        if (!string.Equals(azp, "typing-client", StringComparison.Ordinal) || gty is null)
        {
            Assert.Null(MatchAuthorization.MachineClientId(principal));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PermissionsClaim_AcceptsExactPermission()
    {
        var principal = Principal(
            new Claim("sub", "typing-client@clients"),
            new Claim("azp", "typing-client"),
            new Claim("gty", "client-credentials"),
            new Claim("permissions", "matches.finish"));

        Assert.True(MatchAuthorization.CanFinish(principal));
    }

    private static ClaimsPrincipal Machine(string clientId, string scope) => Principal(
        new Claim("sub", $"{clientId}@clients"),
        new Claim("azp", clientId),
        new Claim("gty", "client-credentials"),
        new Claim("scope", scope));

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));
}
