using System.Security.Claims;
using BattleHub.Matchmaking.Api.Configuration;

namespace BattleHub.Matchmaking.Tests.Configuration;

public class MatchAuthorizationTests
{
    [Fact]
    public void IsUser_RejectsPrincipalWithoutIdentity() => Assert.False(MatchAuthorization.IsUser(new ClaimsPrincipal()));

    [Theory]
    [InlineData("", null, false)]
    [InlineData("user", null, true)]
    [InlineData("user", "client-credentials", false)]
    public void IsUser_ChecksIdentityAndGrantType(string subject, string? grant, bool expected)
    {
        var claims = new List<Claim> { new("sub", subject) };
        if (grant is not null) claims.Add(new Claim("gty", grant));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        Assert.Equal(expected, MatchAuthorization.IsUser(principal));
    }

    [Fact]
    public void IsUser_AcceptsNameIdentifierWhenSubjectIsAbsent()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "test"));
        Assert.True(MatchAuthorization.IsUser(principal));
    }

    [Fact]
    public void IsUser_RejectsClientSubjectEvenWithNameIdentifier()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user"), new Claim("sub", "app@clients")], "test"));
        Assert.False(MatchAuthorization.IsUser(principal));
    }
}
