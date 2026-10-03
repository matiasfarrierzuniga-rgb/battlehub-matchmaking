namespace BattleHub.Matchmaking.Api.Configuration;

public sealed class Auth0Options
{
    public const string SectionName = "Auth0";

    public string Domain { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;
}
