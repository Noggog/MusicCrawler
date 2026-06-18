using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Placeholder acquisition backend: logs the request and accepts it, so the manual "mark ordered"
/// flow advances an item to <see cref="PurchaseStatus.Sent"/> today. Replace with a real target
/// (e.g. Lidarr) behind <see cref="IDownloader"/> without touching the purchase list or its UI.
/// </summary>
public class NoOpDownloader : IDownloader
{
    private readonly ILogger<NoOpDownloader> _logger;

    public NoOpDownloader(ILogger<NoOpDownloader> logger)
    {
        _logger = logger;
    }

    public string Name => "none (manual)";

    public Task<bool> Request(PurchaseItem item)
    {
        var what = item.Album is null ? item.Artist.ArtistName : $"{item.Artist.ArtistName} — {item.Album}";
        _logger.LogInformation("No-op downloader: would acquire {What}", what);
        return Task.FromResult(true);
    }
}
