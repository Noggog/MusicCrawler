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
    private readonly ILogger<PurchaseService> _logger;

    public PurchaseService(
        IPurchaseRepo purchases,
        IUserQueueRepo queue,
        IUserAlbumRatingRepo albumRatings,
        ILibraryProvider library,
        IArtistCatalogRepo catalog,
        IMissingAlbumRepo missing,
        IDownloader downloader,
        ILogger<PurchaseService> logger)
    {
        _purchases = purchases;
        _queue = queue;
        _albumRatings = albumRatings;
        _library = library;
        _catalog = catalog;
        _missing = missing;
        _downloader = downloader;
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

    /// <summary>
    /// Hands an item to the downloader and, if accepted, advances it to <see cref="PurchaseStatus.Sent"/>.
    /// Returns false if the id is unknown or the backend declined.
    /// </summary>
    public async Task<bool> Order(string id)
    {
        var item = (await _purchases.GetAll()).FirstOrDefault(p => p.Id == id);
        if (item is null)
        {
            return false;
        }

        if (!await _downloader.Request(item))
        {
            _logger.LogWarning("Downloader {Name} declined purchase {Id}", _downloader.Name, id);
            return false;
        }

        return await _purchases.SetStatus(id, PurchaseStatus.Sent);
    }

    /// <summary>Moves an ordered item back to <see cref="PurchaseStatus.Pending"/> (undo an order).</summary>
    public Task<bool> Unsend(string id) => _purchases.SetStatus(id, PurchaseStatus.Pending);

    /// <summary>Re-queues a failed item for another download attempt.</summary>
    public Task<bool> Retry(string id) => _purchases.SetStatus(id, PurchaseStatus.Pending);

    /// <summary>Removes an item from the list entirely.</summary>
    public Task Remove(string id) => _purchases.Remove(id);

    /// <summary>
    /// Folds the current liked-but-unowned set into the store and reconciles statuses. Idempotent —
    /// safe to call on every read and after every sync.
    /// </summary>
    public async Task Reconcile()
    {
        var owned = (await _library.GetAllArtistMetadata())
            .Select(a => a.ArtistKey.ArtistName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownedAlbums = await _catalog.GetOwnedAlbums();

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
        ownedAlbums.TryGetValue(artist, out var set) && set.Contains(album);
}
