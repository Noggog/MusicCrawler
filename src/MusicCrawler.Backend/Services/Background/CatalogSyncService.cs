using MusicCrawler.Backend.Services.Singletons;

namespace MusicCrawler.Backend.Services.Background;

/// <summary>
/// Keeps the Library Catalog fresh: syncs once on startup, then daily (jittered, like every recurring
/// wait in the app — see <see cref="JitterPolicy"/>).
/// A failed sync is logged and retried at the next tick — it never takes the app down, since
/// reads serve from whatever is already in the catalog. (Registered as a hosted service in
/// Program.cs rather than via assembly scanning, so it lives outside the scanned namespace.)
/// </summary>
public class CatalogSyncService : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromDays(1);

    private readonly CatalogRefresher _refresher;
    private readonly PurchaseService _purchases;
    private readonly JitterPolicy _jitter;
    private readonly ILogger<CatalogSyncService> _logger;

    public CatalogSyncService(
        CatalogRefresher refresher, PurchaseService purchases, JitterPolicy jitter,
        ILogger<CatalogSyncService> logger)
    {
        _refresher = refresher;
        _purchases = purchases;
        _jitter = jitter;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _jitter.RunPeriodic(TimeSpan.Zero, SyncInterval, SyncOnce, stoppingToken);

    private async Task SyncOnce()
    {
        try
        {
            await _refresher.Refresh();
            // Newly-arrived artists close out their purchase rows (→ in-library, off the buy list).
            await _purchases.Reconcile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled catalog sync failed; will retry at the next interval");
        }
    }
}
