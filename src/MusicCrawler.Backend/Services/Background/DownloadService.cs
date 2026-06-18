using MusicCrawler.Backend.Services.Download;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Background;

/// <summary>
/// The slow, server-controlled download drainer. Deezer cracks down on download tooling, so this is
/// deliberately gentle: one item at a time, a configurable delay between items, and a long pause
/// between batches. It only acts on <see cref="PurchaseStatus.Pending"/> Deezer-album purchases
/// ("albums only"); each is handed to the <see cref="IDownloader"/> and marked
/// <see cref="PurchaseStatus.Sent"/> on success or <see cref="PurchaseStatus.Failed"/> on error. The
/// existing catalog sync closes the loop — once a download lands in Plex, reconcile flips it to
/// in-library and it drops off the list. Disabled (idles) unless downloads are configured.
/// </summary>
public class DownloadService : BackgroundService
{
    private readonly IPurchaseRepo _repo;
    private readonly IDownloader _downloader;
    private readonly DownloaderConfig _config;
    private readonly PurchaseService _purchases;
    private readonly ILogger<DownloadService> _logger;

    public DownloadService(
        IPurchaseRepo repo,
        IDownloader downloader,
        DownloaderConfig config,
        PurchaseService purchases,
        ILogger<DownloadService> logger)
    {
        _repo = repo;
        _downloader = downloader;
        _config = config;
        _purchases = purchases;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("Downloads disabled (DEEZER_DOWNLOADS_ENABLED not set); drainer idle");
            return;
        }

        _logger.LogInformation(
            "Download drainer active via {Backend}: batch {Batch}, {ItemDelay}s between items, {BatchInterval} between batches",
            _downloader.Name, _config.BatchSize, _config.ItemDelay.TotalSeconds, _config.BatchInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Refresh ids/ownership first so we only act on what's still wanted and downloadable.
                await _purchases.Reconcile();
                var processed = await DrainBatch(stoppingToken);
                if (processed > 0)
                {
                    // Drop anything that became owned/unwanted during the batch.
                    await _purchases.Reconcile();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download batch failed; will retry next interval");
            }

            await Delay(_config.BatchInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Downloads up to one batch of pending Deezer-album purchases, oldest first, throttled by the
    /// configured per-item delay. Self-contained (repo + downloader + config only) so it's unit
    /// testable; the surrounding loop owns reconciliation and pacing between batches.
    /// </summary>
    public async Task<int> DrainBatch(CancellationToken ct)
    {
        var pending = (await _repo.GetAll())
            .Where(p => p.Status == PurchaseStatus.Pending
                        && p.Kind == FeedKind.MissingAlbum
                        && p.DeezerAlbumId is > 0)
            .OrderBy(p => p.RequestedAt)
            .Take(_config.BatchSize)
            .ToList();

        if (pending.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < pending.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = pending[i];

            bool ok;
            try
            {
                ok = await _downloader.Request(item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Download errored for {Id}", item.Id);
                ok = false;
            }

            await _repo.SetStatus(item.Id, ok ? PurchaseStatus.Sent : PurchaseStatus.Failed);

            // Space out items (Deezer rate-limits aggressively); no trailing delay after the last.
            if (i < pending.Count - 1)
            {
                await Delay(_config.ItemDelay, ct);
            }
        }

        _logger.LogInformation("Download batch processed {Count} item(s)", pending.Count);
        return pending.Count;
    }

    // Task.Delay throws on a zero/negative TimeSpan only for < -1ms; guard so tests can use zero delays.
    private static Task Delay(TimeSpan delay, CancellationToken ct) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, ct);
}
