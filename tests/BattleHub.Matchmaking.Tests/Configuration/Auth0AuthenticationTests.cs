using BattleHub.Matchmaking.Api.Configuration;
using Microsoft.AspNetCore.Http;

namespace BattleHub.Matchmaking.Tests.Configuration;

public class Auth0AuthenticationTests
{
    [Theory]
    [InlineData("tenant.auth0.com", "https://tenant.auth0.com/")]
    [InlineData("https://tenant.auth0.com", "https://tenant.auth0.com/")]
    [InlineData("https://tenant.auth0.com/", "https://tenant.auth0.com/")]
    [Trait("Category", "Unit")]
    public void NormalizeAuthority_ReturnsHttpsAuthorityWithSingleTrailingSlash(
        string domain,
        string expected)
    {
        Assert.Equal(expected, Auth0Authentication.NormalizeAuthority(domain));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Configure_UsesConfiguredAudienceAndEnablesTokenValidation()
    {
        var jwt = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions();

        Auth0Authentication.Configure(jwt, new Auth0Options
        {
            Domain = "tenant.auth0.com",
            Audience = "matchmaking-api"
        });

        Assert.Equal("matchmaking-api", jwt.Audience);
        Assert.False(jwt.MapInboundClaims);
        Assert.True(jwt.TokenValidationParameters.ValidateIssuer);
        Assert.True(jwt.TokenValidationParameters.ValidateAudience);
        Assert.True(jwt.TokenValidationParameters.ValidateLifetime);
        Assert.True(jwt.TokenValidationParameters.ValidateIssuerSigningKey);
    }

    [Theory]
    [InlineData("/hubs/lobby", "token")]
    [InlineData("/hubs/lobby/negotiate", "token")]
    [InlineData("/api/matches", null)]
    [Trait("Category", "Unit")]
    public void GetSignalRAccessToken_OnlyReadsLobbyHubPath(string path, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?access_token=token");

        Assert.Equal(expected, Auth0Authentication.GetSignalRAccessToken(context.Request));
    }
}
