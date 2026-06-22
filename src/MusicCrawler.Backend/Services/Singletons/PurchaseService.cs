using MusicCrawler.Backend.Services.Download;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Owns the shared "to buy" list and its lifecycle. The list is derived from every user's liked,
/// not-yet-owned artists and missing albums (the unified maintainer queue), but persisted with a
/// status — pending → sent → in-library — so ordering progress survives restarts and isn't
/// recomputed away.
///
/// <see cref="Reconcile"/> is the single sync point: it folds the current liked-but-unowned set into
/// the store, closes out anything that has since arrived in the library (→ InLibrary), and drops
/// pending rows no one wants any more (already-ordered rows are kept — they're in flight). It runs
/// after each catalog/album sync and on each read of the list.
/// </summary>
public class PurchaseService
{
    private readonly IPurchaseRepo _purchases;
    private readonly IUserQueueRepo _queue;
    private readonly IUserAlbumRatingRepo _albumRatings;
    private readonly ILibraryProvider _library;
    private readonly IArtistCatalogRepo _catalog;
    private readonly IMissingAlbumRepo _missing;
    private readonly IDownloader _downloader;
    private readonly DownloaderConfig _config;
    private readonly ILogger<PurchaseService> _logger;

    public PurchaseService(
        IPurchaseRepo purchases,
        IUserQueueRepo queue,
        IUserAlbumRatingRepo albumRatings,
        ILibraryProvider library,
        IArtistCatalogRepo catalog,
        IMissingAlbumRepo missing,
        IDownloader downloader,
        DownloaderConfig config,
        ILogger<PurchaseService> logger)
    {
        _purchases = purchases;
        _queue = queue;
        _albumRatings = albumRatings;
        _library = library;
        _catalog = catalog;
        _missing = missing;
        _downloader = downloader;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// The active acquisition list — everything except items already in the library (so pending, sent
    /// and failed all show), newest first. Reconciles first so the page is always current.
    /// </summary>
    public async Task<PurchaseItem[]> GetActive()
    {
        await Reconcile();
        return (await _purchases.GetAll())
            .Where(p => p.Status != PurchaseStatus.InLibrary)
            .ToArray();
    }

    /// <summary>Moves a downloaded/queued item back to <see cref="PurchaseStatus.Pending"/> (undo).</summary>
    public Task<bool> Unsend(string id) => _purchases.SetStatus(id, PurchaseStatus.Pending);

    /// <summary>
    /// A live snapshot of the download subsystem for the monitoring panel — backend + throttle
    /// config and current counts. Cheap (one read, no reconcile); the list query reconciles. "Queued"
    /// is downloadable albums waiting (wishlist artists don't download, so they're excluded).
    /// </summary>
    public async Task<DownloadSnapshot> GetDownloadSnapshot()
    {
        var all = await _purchases.GetAll();
        var queued = all.Count(p => p.Status == PurchaseStatus.Pending
                                    && p.Kind == FeedKind.MissingAlbum && p.DeezerAlbumId is > 0);
        var current = all
            .Where(p => p.Status == PurchaseStatus.Downloading)
            .OrderBy(p => p.RequestedAt)
            .ToArray();

        return new DownloadSnapshot(
            _config.Automatic,
            _downloader.Name,
            _config.BatchSize,
            _config.ItemDelay.TotalSeconds,
            _config.BatchInterval.TotalMinutes,
            queued,
            current.Length,
            all.Count(p => p.Status == PurchaseStatus.Sent),
            all.Count(p => p.Status == PurchaseStatus.Failed),
            current);
    }

    /// <summary>
    /// Folds the current liked-but-unowned set into the store and reconciles statuses. Idempotent —
    /// safe to call on every read and after every sync.
    /// </summary>
    public async Task Reconcile()
    {
        var owned = (await _library.GetAllArtistMetadata())
            .Select(a => a.ArtistKey.ArtistName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Normalize the owned album titles up front so typography / whitespace / zero-width
        // differences between Plex and Deezer can't keep an already-owned album stuck in the queue.
        // This is the same canonical match the missing-album diff uses, so the two agree.
        var ownedAlbums = (await _catalog.GetOwnedAlbums()).ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(AlbumTitleMatcher.Normalize).ToHashSet(StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        // The Deezer album id per (artist, album) — sourced from the global missing-albums set so a
        // liked album carries the id the downloader needs without threading it through the rating flow.
        var deezerIds = (await _missing.GetAll())
            .GroupBy(m => AlbumRatingKey.For(m.Artist.ArtistName, m.Album.AlbumName))
            .ToDictionary(g => g.Key, g => g.First().DeezerAlbumId);

        // Desired = the current liked-but-unowned items, keyed and deduped across users.
        var desired = new Dictionary<string, PurchaseItem>();

        foreach (var g in (await _queue.GetAllLiked())
                     .Where(c => !owned.Contains(c.Artist.ArtistName))
                     .GroupBy(c => c.Artist.ArtistName, StringComparer.OrdinalIgnoreCase))
        {
            var id = PurchaseKey.ForArtist(g.Key);
            desired[id] = new PurchaseItem(
                id, FeedKind.RecommendedArtist, g.First().Artist, null,
                g.Select(c => c.ImageUrl).FirstOrDefault(u => u != null),
                g.Max(c => c.Score),
                g.SelectMany(c => c.Sources).Distinct().ToArray(),
                PurchaseStatus.Pending, default, null, null);
        }

        foreach (var g in (await _albumRatings.GetAllLiked())
                     .Where(r => !AlbumIsOwned(ownedAlbums, r.Artist.ArtistName, r.Album.AlbumName))
                     .GroupBy(r => PurchaseKey.ForAlbum(r.Artist.ArtistName, r.Album.AlbumName)))
        {
            var first = g.First();
            var ratingKey = AlbumRatingKey.For(first.Artist.ArtistName, first.Album.AlbumName);
            long? deezerAlbumId = deezerIds.TryGetValue(ratingKey, out var did) && did != 0 ? did : null;
            desired[g.Key] = new PurchaseItem(
                g.Key, FeedKind.MissingAlbum, first.Artist, first.Album.AlbumName,
                g.Select(r => r.AlbumArt).FirstOrDefault(a => a != null),
                0, Array.Empty<string>(),
                PurchaseStatus.Pending, default, null, deezerAlbumId);
        }

        // Insert new wants as pending / refresh display fields on existing rows.
        foreach (var item in desired.Values)
        {
            await _purchases.Upsert(item);
        }

        // Close the loop / prune: walk existing rows against ownership + desire.
        foreach (var row in await _purchases.GetAll())
        {
            var nowOwned = row.Kind == FeedKind.MissingAlbum
                ? AlbumIsOwned(ownedAlbums, row.Artist.ArtistName, row.Album ?? "")
                : owned.Contains(row.Artist.ArtistName);

            if (nowOwned)
            {
                if (row.Status != PurchaseStatus.InLibrary)
                {
                    await _purchases.SetStatus(row.Id, PurchaseStatus.InLibrary);
                }
                continue;
            }

            // Not owned and no longer wanted: drop rows that aren't in flight (pending/failed);
            // keep Sent ones (already downloaded, waiting to land in the library).
            if (!desired.ContainsKey(row.Id)
                && row.Status is PurchaseStatus.Pending or PurchaseStatus.Failed)
            {
                await _purchases.Remove(row.Id);
            }
        }
    }

    private static bool AlbumIsOwned(Dictionary<string, HashSet<string>> ownedAlbums, string artist, string album) =>
        ownedAlbums.TryGetValue(artist, out var set) && set.Contains(AlbumTitleMatcher.Normalize(album));
}
