using BattleHub.Matchmaking.Api.Application.Matches;
using BattleHub.Matchmaking.Api.Application.Matches.Cleanup;
using BattleHub.Matchmaking.Api.Configuration;
using BattleHub.Matchmaking.Api.Infrastructure.MongoDb;
using BattleHub.Matchmaking.Api.Infrastructure.MongoDb.Matches;
using BattleHub.Matchmaking.Api.Transport;
using BattleHub.Matchmaking.Api.Transport.Lobby;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<MatchExceptionHandler>();

var corsOptions = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>() ?? new CorsOptions();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsOptions.SectionName, policy =>
    {
        policy
            .WithOrigins(corsOptions.Origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services
    .AddOptions<Auth0Options>()
    .Bind(builder.Configuration.GetSection(Auth0Options.SectionName))
    .Validate(static options => !string.IsNullOrWhiteSpace(options.Domain), "Auth0:Domain is required.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.Audience), "Auth0:Audience is required.")
    .ValidateOnStart();

var auth0Options = builder.Configuration
    .GetSection(Auth0Options.SectionName)
    .Get<Auth0Options>() ?? new Auth0Options();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => Auth0Authentication.Configure(options, auth0Options));
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(MatchAuthorization.UserPolicy, policy =>
        policy.RequireAssertion(context => MatchAuthorization.IsUser(context.User)));
    options.AddPolicy(MatchAuthorization.FinishPolicy, policy =>
        policy.RequireAssertion(context => MatchAuthorization.CanFinish(context.User)));
});

builder.Services
    .AddOptions<GameServiceOptions>()
    .Bind(builder.Configuration.GetSection(GameServiceOptions.SectionName));
builder.Services
    .AddOptions<CleanupOptions>()
    .Bind(builder.Configuration.GetSection(CleanupOptions.SectionName))
    .Validate(static options => options.IntervalSeconds > 0, "Cleanup:IntervalSeconds must be greater than zero.")
    .Validate(static options => options.HeartbeatTimeoutSeconds > 0, "Cleanup:HeartbeatTimeoutSeconds must be greater than zero.")
    .Validate(static options => options.InactivitySeconds > 0, "Cleanup:InactivitySeconds must be greater than zero.")
    .Validate(static options => options.ExpirationSeconds > 0, "Cleanup:ExpirationSeconds must be greater than zero.")
    .Validate(static options => options.RetentionSeconds > 0, "Cleanup:RetentionSeconds must be greater than zero.")
    .ValidateOnStart();

builder.Services
    .AddOptions<MongoDbOptions>()
    .Bind(builder.Configuration.GetSection(MongoDbOptions.SectionName))
    .Validate(
        static options => !string.IsNullOrWhiteSpace(options.ConnectionString),
        $"{MongoDbOptions.SectionName}:ConnectionString is required.")
    .Validate(
        static options => !string.IsNullOrWhiteSpace(options.DatabaseName),
        $"{MongoDbOptions.SectionName}:DatabaseName is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<MongoDbOptions>>().Value;
    return new MongoClient(options.ConnectionString);
});

builder.Services.AddSingleton<IMongoDatabase>(serviceProvider =>
{
    var client = serviceProvider.GetRequiredService<IMongoClient>();
    var options = serviceProvider.GetRequiredService<IOptions<MongoDbOptions>>().Value;
    return client.GetDatabase(options.DatabaseName);
});

builder.Services.AddSingleton<MatchmakingMongoContext>();
builder.Services.AddSingleton<IMatchStore, MongoMatchStore>();
builder.Services.AddSingleton<IMatchCleanupStore, MongoMatchCleanupStore>();
builder.Services.AddSingleton<IMatchEventPublisher, SignalRMatchEventPublisher>();
builder.Services.AddSingleton<MatchService>();
builder.Services.AddSingleton<FinishMatchService>();
builder.Services.AddSingleton<MatchCleanupService>();
builder.Services.AddHostedService<MatchCleanupBackgroundService>();

var app = builder.Build();

app.UseExceptionHandler();

app.UseCors(CorsOptions.SectionName);
app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck");

app.MapControllers();
app.MapHub<LobbyHub>("/hubs/lobby");

app.Run();

public partial class Program;
