using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Download;

/// <summary>
/// Placeholder acquisition backend, used when downloads are disabled (no <c>DEEZER_DOWNLOADS_ENABLED</c>).
/// Logs the request and accepts it, so the manual "order" flow still advances an item to
/// <see cref="PurchaseStatus.Sent"/>. The real path is <see cref="StreamripDownloader"/>.
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
