using System.Security.Claims;

namespace BattleHub.Matchmaking.Api.Configuration;

public sealed class GameServiceOptions
{
    public const string SectionName = "GameServices";

    public Dictionary<string, string> Clients { get; init; } = new(StringComparer.Ordinal);
}

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
    public const string FinishPolicy = nameof(FinishPolicy);
    public const string FinishPermission = "matches.finish";

    public static string? UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub");

    public static string? MachineClientId(ClaimsPrincipal principal)
    {
        if (!string.Equals(principal.FindFirstValue("gty"), "client-credentials", StringComparison.Ordinal))
        {
            return null;
        }

        var clientId = principal.FindFirstValue("azp");
        var subject = principal.FindFirstValue("sub");
        return !string.IsNullOrEmpty(clientId)
            && string.Equals(subject, $"{clientId}@clients", StringComparison.Ordinal)
                ? clientId
                : null;
    }

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

    public static bool CanFinish(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true
        && MachineClientId(principal) is not null
        && HasPermission(principal, FinishPermission);

    private static bool HasPermission(ClaimsPrincipal principal, string permission) =>
        principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Concat(principal.FindAll("permissions").Select(claim => claim.Value))
            .Any(value => string.Equals(value, permission, StringComparison.Ordinal));
}
