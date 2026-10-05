using BattleHub.Matchmaking.Api.Configuration;
using Microsoft.Extensions.Options;

namespace BattleHub.Matchmaking.Api.Application.Matches.Cleanup;

public sealed class MatchCleanupBackgroundService(
    MatchCleanupService cleanupService,
    IOptions<CleanupOptions> cleanupOptions,
    ILogger<MatchCleanupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = cleanupOptions.Value;
        if (!options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await cleanupService.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "An error occurred during match cleanup.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
