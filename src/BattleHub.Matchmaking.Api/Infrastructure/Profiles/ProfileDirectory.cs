using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace BattleHub.Matchmaking.Api.Infrastructure.Profiles;

public sealed class ProfileOptions
{
    public const string SectionName = "Profile";

    public string BaseUrl { get; init; } = string.Empty;
}

public interface IProfileDirectory
{
    // Nombre visible del usuario autenticado, según Profile Service; null si no se pudo obtener.
    Task<string?> GetCurrentDisplayNameAsync(string? authorizationHeader, CancellationToken cancellationToken);
}

public sealed class HttpProfileDirectory(
    HttpClient http,
    IOptions<ProfileOptions> options,
    ILogger<HttpProfileDirectory> logger) : IProfileDirectory
{
    private const int MaxDisplayNameLength = 100;

    public async Task<string?> GetCurrentDisplayNameAsync(
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var baseUrl)
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(baseUrl, "/api/profiles/me"));
            request.Headers.Authorization = authorization;
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Profile Service respondió {Status} al consultar el perfil.", (int)response.StatusCode);
                return null;
            }

            var profile = await response.Content.ReadFromJsonAsync<ProfileDto>(cancellationToken);
            var displayName = profile?.DisplayName?.Trim();
            return string.IsNullOrEmpty(displayName)
                ? null
                : displayName[..Math.Min(displayName.Length, MaxDisplayNameLength)];
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("No se pudo consultar Profile Service: {Message}", exception.Message);
            return null;
        }
    }

    private sealed record ProfileDto(string? DisplayName);
}
