using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace BattleHub.Matchmaking.Api.Configuration;

public static class Auth0Authentication
{
    public static string NormalizeAuthority(string domain)
    {
        var value = domain.Trim().TrimEnd('/');
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = $"https://{value}";
        }

        return $"{value}/";
    }

    public static string? GetSignalRAccessToken(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/hubs/lobby"))
        {
            return null;
        }

        var token = request.Query["access_token"].ToString();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public static void Configure(JwtBearerOptions jwtOptions, Auth0Options auth0Options)
    {
        jwtOptions.Authority = NormalizeAuthority(auth0Options.Domain);
        jwtOptions.Audience = auth0Options.Audience;
        jwtOptions.TokenValidationParameters.ValidateIssuer = true;
        jwtOptions.TokenValidationParameters.ValidateAudience = true;
        jwtOptions.TokenValidationParameters.ValidateLifetime = true;
        jwtOptions.TokenValidationParameters.ValidateIssuerSigningKey = true;
        jwtOptions.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = GetSignalRAccessToken(context.Request);
                return Task.CompletedTask;
            }
        };
    }
}
