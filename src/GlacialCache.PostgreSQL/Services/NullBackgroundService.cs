using Microsoft.Extensions.Hosting;

namespace GlacialCache.PostgreSQL.Services;

/// <summary>
/// No-operation background service used when cleanup is disabled.
/// </summary>
internal class NullBackgroundService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No-op - cleanup is disabled
        return Task.CompletedTask;
    }
}
