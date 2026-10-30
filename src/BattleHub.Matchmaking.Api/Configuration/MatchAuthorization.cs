using System.Security.Claims;

namespace BattleHub.Matchmaking.Api.Configuration;

public sealed class CleanupOptions
{
    public const string SectionName = "Cleanup";

    public bool Enabled { get; init; }
    public int IntervalSeconds { get; init; } = 30;
    public int HeartbeatTimeoutSeconds { get; init; } = 120;
    public int InactivitySeconds { get; init; } = 300;
    public int ExpirationSeconds { get; init; } = 7200;
    public int RetentionSeconds { get; init; } = 600;
}

public static class MatchAuthorization
{
    public const string UserPolicy = nameof(UserPolicy);

    public static string? UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub");

    public static bool IsUser(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(UserId(principal)))
        {
            return false;
        }

        var subject = principal.FindFirstValue("sub");
        return !string.Equals(principal.FindFirstValue("gty"), "client-credentials", StringComparison.Ordinal)
            && (subject is null || !subject.EndsWith("@clients", StringComparison.Ordinal));
    }

}
