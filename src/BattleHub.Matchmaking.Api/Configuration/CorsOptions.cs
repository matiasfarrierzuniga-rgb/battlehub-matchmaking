namespace BattleHub.Matchmaking.Api.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] Origins { get; init; } = [];
}
