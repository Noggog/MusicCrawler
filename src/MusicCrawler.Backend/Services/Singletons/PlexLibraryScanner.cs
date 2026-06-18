using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using MusicCrawler.Interfaces;
using MusicCrawler.Plex.Services.Singletons;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// <see cref="ILibraryScanner"/> over Plex. Asks Plex to rescan the music section after albums land so
/// the catalog (and the <see cref="PurchaseStatus.InLibrary"/> flip) updates promptly instead of
/// waiting for the daily refresh.
///
/// <para><b>Debounced via Rx.</b> Downloads drain one at a time, so a batch produces a burst of
/// <see cref="RequestScan"/> calls. Each pushes onto a <see cref="Subject{T}"/>; <c>Throttle</c> (Rx's
/// trailing debounce) emits one value only after <see cref="LibraryScannerConfig.Debounce"/> of
/// silence, and <c>Concat</c> serializes the resulting scans so they never overlap. Net effect: one
/// scan shortly after the batch goes quiet, however many albums it held.</para>
///
/// <para>Off unless <c>PLEX_RESCAN_AFTER_DOWNLOAD</c> is set — when disabled no pipeline is built and
/// <see cref="RequestScan"/> is a no-op. Scan failures are logged, never thrown — a rescan is
/// best-effort and must not disturb the download loop.</para>
/// </summary>
public class PlexLibraryScanner : ILibraryScanner, IDisposable
{
    private readonly PlexApi _plexApi;
    private readonly LibraryScannerConfig _config;
    private readonly ILogger<PlexLibraryScanner> _logger;

    private readonly Subject<Unit> _requests = new();
    private readonly IDisposable? _subscription;

    public PlexLibraryScanner(PlexApi plexApi, LibraryScannerConfig config, ILogger<PlexLibraryScanner> logger)
        : this(plexApi, config, logger, DefaultScheduler.Instance)
    {
    }

    /// <summary>Scheduler-injecting ctor so the debounce clock is deterministic under a TestScheduler.</summary>
    protected PlexLibraryScanner(
        PlexApi plexApi, LibraryScannerConfig config, ILogger<PlexLibraryScanner> logger, IScheduler scheduler)
    {
        _plexApi = plexApi;
        _config = config;
        _logger = logger;

        if (_config.Enabled)
        {
            _subscription = _requests
                .Throttle(_config.Debounce, scheduler)
                .Select(_ => Observable.FromAsync(ScanSafely))
                .Concat()
                .Subscribe();
        }
    }

    public Task RequestScan()
    {
        if (_config.Enabled)
        {
            _requests.OnNext(Unit.Default);
        }
        return Task.CompletedTask;
    }

    private async Task ScanSafely()
    {
        try
        {
            await Scan();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Targeted Plex rescan failed");
        }
    }

    /// <summary>The actual Plex hit. Virtual so the debounce pipeline can be unit-tested without HTTP.</summary>
    protected virtual async Task Scan()
    {
        var library = await _plexApi.ResolveLibrary();
        await _plexApi.RefreshLibrary(library.Key);
        _logger.LogInformation(
            "Triggered targeted Plex rescan of library {Library} ({Key}) after download activity",
            library.Title, library.Key);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _requests.Dispose();
    }
}
