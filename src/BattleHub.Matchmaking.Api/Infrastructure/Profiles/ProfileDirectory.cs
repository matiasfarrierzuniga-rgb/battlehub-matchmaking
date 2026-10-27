using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
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

    // Permisos del usuario autenticado. Si Profile no puede confirmarlos, lanza ProfileUnavailableException.
    Task<IReadOnlyList<string>> GetCurrentPermissionsAsync(string? authorizationHeader, CancellationToken cancellationToken);
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

    public async Task<IReadOnlyList<string>> GetCurrentPermissionsAsync(
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateAuthorizedRequest(authorizationHeader, "/api/profiles/me/permissions", out var request))
        {
            logger.LogWarning("No se pudieron consultar permisos: falta la URL de Profile o un token Bearer.");
            throw new ProfileUnavailableException();
        }

        try
        {
            using (request)
            {
                using var response = await http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<PermissionsDto>(cancellationToken);
                    return body?.Permissions ?? [];
                }

                logger.LogWarning(
                    "Profile Service respondió {Status} al consultar permisos.",
                    (int)response.StatusCode);
                if ((int)response.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    throw new ProfileUnavailableException();
                }

                throw new UnauthorizedAccessException("Profile Service did not grant the required permissions.");
            }
        }
        catch (Exception exception) when (exception is not (ProfileUnavailableException or UnauthorizedAccessException)
            && !cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("No se pudieron consultar los permisos en Profile Service: {Message}", exception.Message);
            throw new ProfileUnavailableException();
        }
    }

    private bool TryCreateAuthorizedRequest(
        string? authorizationHeader,
        string path,
        out HttpRequestMessage request)
    {
        request = null!;
        if (!Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var baseUrl)
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return false;
        }

        request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, path));
        request.Headers.Authorization = authorization;
        return true;
    }

    private sealed record ProfileDto(string? DisplayName);

    private sealed record PermissionsDto(List<string>? Permissions);
}
