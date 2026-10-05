using System.Text.Json;

namespace BattleHub.Matchmaking.Tests.Configuration;

public class GameServiceClientConfigurationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void AppSettings_ResolvesTypingTriviaAndMemoryClients()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindAppSettings()));
        var clients = document.RootElement.GetProperty("GameServices").GetProperty("Clients");

        Assert.Equal("typing", clients.GetProperty("8BWcE4T8HxhJrxU1CtgmNkjDOpkrN4Su").GetString());
        Assert.Equal("trivia", clients.GetProperty("xYhNYG9lOGLA6KvEM0Y4cccH2f7JDD5x").GetString());
        Assert.Equal("memory", clients.GetProperty("lfcBlgOCs9N0w6F6AWl4FqOs4xF9AQiN").GetString());
        Assert.False(clients.TryGetProperty("unknown-client", out _));
    }

    private static string FindAppSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "BattleHub.Matchmaking.Api",
                "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("appsettings.json was not found.");
    }
}
