using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using MusicCrawler.Backend.Services.Singletons;

namespace MusicCrawler.Backend.Services.Background;

/// <summary>
/// Keeps the Library Catalog fresh: syncs once on startup, then on a fixed daily interval.
/// A failed sync is logged and retried at the next tick — it never takes the app down, since
/// reads serve from whatever is already in the catalog. (Registered as a hosted service in
/// Program.cs rather than via assembly scanning, so it lives outside the scanned namespace.)
/// </summary>
public class CatalogSyncService : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromDays(1);

    private readonly CatalogRefresher _refresher;
    private readonly ILogger<CatalogSyncService> _logger;

    public CatalogSyncService(CatalogRefresher refresher, ILogger<CatalogSyncService> logger)
    {
        _refresher = refresher;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Observable
            .Timer(TimeSpan.Zero, SyncInterval)
            .SelectMany(_ => Observable.FromAsync(SyncOnce))
            .ToTask(stoppingToken);
    }

    private async Task SyncOnce()
    {
        try
        {
            await _refresher.Refresh();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled catalog sync failed; will retry at the next interval");
        }
    }
}
