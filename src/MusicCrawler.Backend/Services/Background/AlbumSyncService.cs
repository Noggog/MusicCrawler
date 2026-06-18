using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using MusicCrawler.Backend.Services.Singletons;

namespace MusicCrawler.Backend.Services.Background;

/// <summary>
/// Keeps the missing-album set fresh: runs the Deezer discography diff shortly after startup, then
/// daily. Deliberately offset from the Plex catalog sync so the catalog (its input) is populated
/// first, and so the two Deezer-heavy / Plex-heavy passes don't contend on boot. A failed run is
/// logged and retried next tick — the per-user feed serves whatever was last persisted.
/// </summary>
public class AlbumSyncService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SyncInterval = TimeSpan.FromDays(1);

    private readonly MissingAlbumRefresher _refresher;
    private readonly PurchaseService _purchases;
    private readonly ILogger<AlbumSyncService> _logger;

    public AlbumSyncService(
        MissingAlbumRefresher refresher, PurchaseService purchases, ILogger<AlbumSyncService> logger)
    {
        _refresher = refresher;
        _purchases = purchases;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Observable
            .Timer(StartupDelay, SyncInterval)
            .SelectMany(_ => Observable.FromAsync(SyncOnce))
            .ToTask(stoppingToken);
    }

    private async Task SyncOnce()
    {
        try
        {
            await _refresher.Refresh();
            // Albums that have since landed in the library close out their purchase rows.
            await _purchases.Reconcile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled missing-album sync failed; will retry at the next interval");
        }
    }
}
