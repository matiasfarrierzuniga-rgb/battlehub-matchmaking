using BattleHub.Matchmaking.Api.Infrastructure.Profiles;

namespace BattleHub.Matchmaking.Tests.Support;

internal sealed class GrantingProfileDirectory : IProfileDirectory
{
    public static readonly IReadOnlyList<string> AllPermissions =
    [
        "matches.create",
        "games.typing.play",
        "games.trivia.play",
        "games.memory.play"
    ];

    public string? DisplayName { get; set; }

    public IReadOnlyList<string> Permissions { get; set; } = AllPermissions;

    public Exception? PermissionsError { get; set; }

    public string? ReceivedAuthorization { get; private set; }

    public Task<string?> GetCurrentDisplayNameAsync(string? authorizationHeader, CancellationToken cancellationToken)
    {
        ReceivedAuthorization = authorizationHeader;
        return Task.FromResult(DisplayName);
    }

    public Task<IReadOnlyList<string>> GetCurrentPermissionsAsync(
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        ReceivedAuthorization = authorizationHeader;
        if (PermissionsError is not null)
        {
            throw PermissionsError;
        }

        return Task.FromResult(Permissions);
    }
}
