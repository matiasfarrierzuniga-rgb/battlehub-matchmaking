namespace BattleHub.Matchmaking.Api.Infrastructure.Profiles;

public sealed class ProfileUnavailableException : Exception
{
    public ProfileUnavailableException()
        : base("Profile Service no está disponible.")
    {
    }
}
