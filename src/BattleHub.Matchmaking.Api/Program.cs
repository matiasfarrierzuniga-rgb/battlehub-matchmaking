using BattleHub.Matchmaking.Api.Infrastructure.MongoDb;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck");

app.Run();

public partial class Program;
