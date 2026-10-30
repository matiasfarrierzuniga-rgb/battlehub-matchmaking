using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Transport.Matches;
using BattleHub.Matchmaking.Tests.Support;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace BattleHub.Matchmaking.Tests;

public class JwtBearerIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ValidUserTokens_CreateJoinStartAndFinishWithoutProfile()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("valid", "host"));
        using var createdResponse = await client.PostAsJsonAsync("/api/matches", new { title = "Room", gameType = "memory", maxPlayers = 4, userId = "forged" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<MatchResponse>())!;
        Assert.Equal("host", created.CreatedBy);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/matches")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/matches/{created.Id}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("valid", "guest"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/matches/{created.Id}/join", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("valid", "host"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/matches/{created.Id}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/matches/{created.Id}/finish", null)).StatusCode);
    }

    [Theory]
    [InlineData("valid", HttpStatusCode.OK)]
    [InlineData("missing", HttpStatusCode.Unauthorized)]
    [InlineData("invalid", HttpStatusCode.Unauthorized)]
    [InlineData("signature", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("issuer", HttpStatusCode.Unauthorized)]
    [InlineData("audience", HttpStatusCode.Unauthorized)]
    [Trait("Category", "Integration")]
    public async Task BearerValidation_EnforcesAllChecks(string scenario, HttpStatusCode expected)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/matches");
        if (scenario != "missing")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scenario == "invalid" ? "not-a-jwt" : factory.Token(scenario));
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}. Authentication: {factory.AuthenticationError}");
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly RSA _rsa = RSA.Create(2048);
        public string? AuthenticationError { get; private set; }
        private const string Issuer = "https://test.auth0.invalid/";
        private const string Audience = "test-audience";

        public string Token(string scenario, string userId = "user-1")
        {
            using var otherKey = RSA.Create(2048);
            var key = new RsaSecurityKey(scenario == "signature" ? otherKey : _rsa) { KeyId = "test-key" };
            var token = new JwtSecurityToken(
                scenario == "issuer" ? "https://wrong.invalid/" : Issuer,
                scenario == "audience" ? "wrong-audience" : Audience,
                [new Claim("sub", userId)],
                DateTime.UtcNow.AddHours(-2),
                scenario == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
                new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth0:Domain"] = "test.auth0.invalid",
                ["Auth0:Audience"] = Audience,
                ["Cleanup:Enabled"] = "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMatchStore>();
                services.AddSingleton<IMatchStore>(new TestMatchStore());
                services.RemoveAll<IMatchHistoryStore>();
                services.AddSingleton<IMatchHistoryStore>(new MemoryMatchHistoryStore());
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    configuration.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "test-key" });
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.TokenValidationParameters.ValidAudience = Audience;
                    options.Events.OnAuthenticationFailed = context =>
                    {
                        AuthenticationError = context.Exception.ToString();
                        return Task.CompletedTask;
                    };
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _rsa.Dispose();
        }
    }
}
