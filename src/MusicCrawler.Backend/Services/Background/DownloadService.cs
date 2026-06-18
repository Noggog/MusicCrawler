using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Channels;
using MusicCrawler.Backend.Services.Download;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Background;

/// <summary>
/// The slow, server-controlled download engine. A single consumer loop pulls album ids off a queue
/// and downloads them one at a time (single-flight — Deezer cracks down on parallel tooling), with a
/// configurable delay between items. Ids reach the queue two ways:
///   • <b>Automatic</b> (when <c>DEEZER_DOWNLOADS_AUTOMATIC</c> is on): a background loop enqueues
///     pending albums every <c>DOWNLOAD_BATCH_INTERVAL_MINUTES</c>.
///   • <b>Manual</b>: <see cref="RequestDownload"/> (the "Download now" button) enqueues one id and
///     returns immediately, so the HTTP request never blocks on the multi-minute fetch.
/// Each item goes Pending → Downloading → Sent/Failed; the catalog sync then closes the loop
/// (file lands in Plex → reconcile → in-library, drops off the list). Registered as a shared
/// singleton hosted service so the endpoint and the loop are the same instance.
/// </summary>
public class DownloadService : BackgroundService
{
    private readonly IPurchaseRepo _repo;
    private readonly IDownloader _downloader;
    private readonly DownloaderConfig _config;
    private readonly PurchaseService _purchases;
    private readonly ILibraryScanner _scanner;
    private readonly ILogger<DownloadService> _logger;

    // Unbounded but effectively tiny; ProcessOne dedups by re-checking status, so duplicate ids are cheap.
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();

    public DownloadService(
        IPurchaseRepo repo,
        IDownloader downloader,
        DownloaderConfig config,
        PurchaseService purchases,
        ILibraryScanner scanner,
        ILogger<DownloadService> logger)
    {
        _repo = repo;
        _downloader = downloader;
        _config = config;
        _purchases = purchases;
        _scanner = scanner;
        _logger = logger;
    }

    /// <summary>
    /// Manually queues one item for download now (the "Download now"/"Retry" button). Resets it to
    /// Pending (so a failed item retries) and enqueues it; returns false if it's unknown or not a
    /// downloadable Deezer album. Non-blocking — the consumer loop does the actual fetch.
    /// </summary>
    public async Task<bool> RequestDownload(string id)
    {
        var item = (await _repo.GetAll()).FirstOrDefault(p => p.Id == id);
        if (item is null || item.Kind != FeedKind.MissingAlbum || item.DeezerAlbumId is null or 0)
        {
            return false;
        }
        if (item.Status == PurchaseStatus.Downloading)
        {
            return true; // already in flight
        }

        await _repo.SetStatus(id, PurchaseStatus.Pending);
        _queue.Writer.TryWrite(id);
        _logger.LogInformation("Manual download requested for {Id}", id);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Recover from a crash mid-download: anything left marked Downloading never finished.
        await ResetStuckDownloads();

        _logger.LogInformation(
            "Download engine ready via {Backend}; automatic={Automatic} (batch {Batch}, {ItemDelay}s/item, every {Interval})",
            _downloader.Name, _config.Automatic, _config.BatchSize, _config.ItemDelay.TotalSeconds, _config.BatchInterval);

        // Run the consumer and the automatic producer together; either ending cancels the other.
        await Task.WhenAll(Consume(stoppingToken), AutoEnqueue(stoppingToken));
    }

    /// <summary>Single-flight consumer: downloads queued ids one at a time, throttled.</summary>
    private async Task Consume(CancellationToken ct)
    {
        try
        {
            await foreach (var id in _queue.Reader.ReadAllAsync(ct))
            {
                var downloaded = await ProcessOne(id);
                if (downloaded)
                {
                    // Drop anything that became owned/unwanted, and space out fetches.
                    await _purchases.Reconcile();
                    // Ask Plex to pick up the new album. Debounced, so a draining batch triggers a
                    // single rescan once it quiets — and a no-op unless PLEX_RESCAN_AFTER_DOWNLOAD is on.
                    await _scanner.RequestScan();
                    await Delay(_config.ItemDelay, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    /// <summary>
    /// Downloads one queued id if it's still a pending downloadable album (re-checked here, so
    /// duplicate/auto+manual enqueues of the same id are harmless). Returns whether a fetch ran.
    /// </summary>
    public async Task<bool> ProcessOne(string id)
    {
        var item = (await _repo.GetAll()).FirstOrDefault(p => p.Id == id);
        if (item is null
            || item.Status != PurchaseStatus.Pending
            || item.Kind != FeedKind.MissingAlbum
            || item.DeezerAlbumId is null or 0)
        {
            return false;
        }

        // Mark it in-flight before the (slow) fetch so the monitor shows what's downloading now.
        await _repo.SetStatus(item.Id, PurchaseStatus.Downloading);

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
        return true;
    }

    /// <summary>When automatic is on, periodically enqueues pending downloadable albums (batch-capped).
    /// Mirrors the daily sync services' Rx shape; off entirely when automatic is disabled.</summary>
    private Task AutoEnqueue(CancellationToken ct)
    {
        if (!_config.Automatic)
        {
            return Task.CompletedTask; // manual "download now" still works via the channel
        }

        return Observable
            .Timer(TimeSpan.Zero, _config.BatchInterval)
            .SelectMany(_ => Observable.FromAsync(EnqueuePendingBatch))
            .ToTask(ct);
    }

    private async Task EnqueuePendingBatch()
    {
        try
        {
            await _purchases.Reconcile();
            var pending = (await _repo.GetAll())
                .Where(p => p.Status == PurchaseStatus.Pending
                            && p.Kind == FeedKind.MissingAlbum
                            && p.DeezerAlbumId is > 0)
                .OrderBy(p => p.RequestedAt)
                .Take(_config.BatchSize);
            foreach (var item in pending)
            {
                _queue.Writer.TryWrite(item.Id);
            }
        }
        catch (Exception ex)
        {
            // A transient failure must not tear down the timer — retry at the next interval.
            _logger.LogWarning(ex, "Auto-enqueue pass failed; will retry at the next interval");
        }
    }

    /// <summary>Returns rows stranded in <see cref="PurchaseStatus.Downloading"/> (e.g. by a crash)
    /// to <see cref="PurchaseStatus.Pending"/> so they're retried.</summary>
    public async Task ResetStuckDownloads()
    {
        foreach (var item in (await _repo.GetAll()).Where(p => p.Status == PurchaseStatus.Downloading))
        {
            await _repo.SetStatus(item.Id, PurchaseStatus.Pending);
            _logger.LogInformation("Reset stranded download {Id} to pending", item.Id);
        }
    }

    // Task.Delay throws on a negative TimeSpan; treat zero/negative as "no wait" so tests run instantly.
    private static Task Delay(TimeSpan delay, CancellationToken ct) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, ct);
}
