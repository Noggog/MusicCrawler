using MusicCrawler.Interfaces;
using MusicCrawler.Plex.Services;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>One combined-name entry the cleanup can resolve, with a preview of the split result.</summary>
/// <param name="Scope">"catalog", "artistRating", or "albumRating".</param>
/// <param name="Name">The offending ';'-joined artist name.</param>
/// <param name="Album">The album, for album-rating entries; null otherwise.</param>
/// <param name="SplitInto">The artist names <paramref name="Name"/> resolves to.</param>
/// <param name="Affected">How many docs this entry covers (ratings aggregate across users).</param>
public record CombinedNameEntry(
    string Scope, string Name, string? Album, IReadOnlyList<string> SplitInto, int Affected);

/// <summary>Counts of what a cleanup run changed.</summary>
public record CleanupResult(int CatalogSplit, int ArtistRatingsSplit, int AlbumRatingsSplit, int PendingRemoved);

/// <summary>
/// Cleans up Plex's ';'-joined multi-artist names (e.g. "Nina Simone;Hot Chip") that leaked into the
/// catalog and user ratings before <see cref="ArtistNames"/> splitting was applied at ingestion.
/// Catalog docs are split in place; rating verdicts are re-created for each real artist (preserving
/// the verdict) and the combined row removed. New syncs no longer produce these, so this is a
/// maintenance sweep rather than an ongoing process.
/// </summary>
public class LibraryCleanupService
{
    private readonly IArtistCatalogRepo _catalog;
    private readonly IUserQueueRepo _queue;
    private readonly IUserAlbumRatingRepo _albumRatings;

    public LibraryCleanupService(
        IArtistCatalogRepo catalog, IUserQueueRepo queue, IUserAlbumRatingRepo albumRatings)
    {
        _catalog = catalog;
        _queue = queue;
        _albumRatings = albumRatings;
    }

    /// <summary>Lists every combined-name entry across the catalog and user ratings, with its split preview.</summary>
    public async Task<IReadOnlyList<CombinedNameEntry>> Scan()
    {
        var entries = new List<CombinedNameEntry>();

        foreach (var name in await _catalog.FindCombinedArtistNames())
        {
            entries.Add(new CombinedNameEntry("catalog", name, null, Split(name), 1));
        }

        foreach (var g in (await _queue.FindCombinedRatings())
                 .GroupBy(r => r.Artist, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(new CombinedNameEntry("artistRating", g.Key, null, Split(g.Key), g.Count()));
        }

        foreach (var g in (await _albumRatings.FindCombinedRatings())
                 .GroupBy(r => (r.Artist, r.Album)))
        {
            entries.Add(new CombinedNameEntry(
                "albumRating", g.Key.Artist, g.Key.Album, Split(g.Key.Artist), g.Count()));
        }

        return entries;
    }

    /// <summary>Resolves every combined-name entry: splits catalog docs and re-attributes ratings.</summary>
    public async Task<CleanupResult> Resolve()
    {
        var now = DateTimeOffset.UtcNow;
        int catalogSplit = 0, artistSplit = 0, albumSplit = 0, pendingRemoved = 0;

        foreach (var name in await _catalog.FindCombinedArtistNames())
        {
            var parts = Split(name);
            if (parts.Count == 0)
            {
                continue;
            }

            await _catalog.SplitCombinedArtist(name, parts, now);
            catalogSplit++;
        }

        foreach (var r in await _queue.FindCombinedRatings())
        {
            var parts = Split(r.Artist);
            // Pending rows are stale recommendations — just drop them (they regenerate from the graph).
            if (r.Status == DiscoveryStatus.Pending || parts.Count == 0)
            {
                await _queue.ClearVerdict(r.UserId, r.Artist);
                pendingRemoved++;
                continue;
            }

            foreach (var part in parts)
            {
                await _queue.Rate(r.UserId, part, r.Status, r.ImageUrl);
            }

            await _queue.ClearVerdict(r.UserId, r.Artist);
            artistSplit++;
        }

        foreach (var r in await _albumRatings.FindCombinedRatings())
        {
            var parts = Split(r.Artist);
            foreach (var part in parts)
            {
                await _albumRatings.Rate(r.UserId, part, r.Album, r.AlbumArt, r.Status);
            }

            await _albumRatings.Clear(r.UserId, r.Artist, r.Album);
            albumSplit++;
        }

        return new CleanupResult(catalogSplit, artistSplit, albumSplit, pendingRemoved);
    }

    private static IReadOnlyList<string> Split(string name) => ArtistNames.Split(name).ToList();
}
