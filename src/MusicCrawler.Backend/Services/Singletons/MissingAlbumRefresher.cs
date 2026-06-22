using MusicCrawler.Deezer.Services;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>The outcome of one missing-album sync pass.</summary>
public record MissingAlbumSyncResult(int ArtistsScanned, int MissingTotal);

/// <summary>One album from an artist's Deezer discography, flagged with whether the library owns it.</summary>
public record DiscographyAlbum(string Title, string? CoverUrl, long? DeezerAlbumId, bool Owned);

/// <summary>
/// The missing-album sync job: for each owned artist, pulls its Deezer discography and diffs it
/// against the albums already in the library, persisting the gap into <see cref="IMissingAlbumRepo"/>.
/// This is the only path that touches Deezer for albums; the per-user "missing albums" feed reads
/// the persisted result, so it works even when Deezer is down. Heavy (one discography call per owned
/// artist), so it runs on its own schedule, separate from the cheap Plex catalog refresh.
/// </summary>
public class MissingAlbumRefresher
{
    // Deezer marks LPs as record_type "album"; "single"/"ep"/"compilation" are filtered out so the
    // feed isn't drowned in singles and reissues.
    private const string AlbumRecordType = "album";

    private readonly IArtistCatalogRepo _catalog;
    private readonly DeezerArtistResolver _resolver;
    private readonly IDeezerApi _deezer;
    private readonly IMissingAlbumRepo _missing;
    private readonly ILogger<MissingAlbumRefresher> _logger;

    public MissingAlbumRefresher(
        IArtistCatalogRepo catalog,
        DeezerArtistResolver resolver,
        IDeezerApi deezer,
        IMissingAlbumRepo missing,
        ILogger<MissingAlbumRefresher> logger)
    {
        _catalog = catalog;
        _resolver = resolver;
        _deezer = deezer;
        _missing = missing;
        _logger = logger;
    }

    public async Task<MissingAlbumSyncResult> Refresh()
    {
        var present = await _catalog.GetAllPresent();
        var ownedAlbums = await _catalog.GetOwnedAlbums();

        var scanned = 0;
        var missingTotal = 0;

        foreach (var artist in present)
        {
            scanned++;
            var owned = ownedAlbums.TryGetValue(artist.ArtistKey.ArtistName, out var set)
                ? set
                : Enumerable.Empty<string>();
            missingTotal += (await RefreshOne(artist.ArtistKey, owned)).Count;
        }

        _logger.LogInformation(
            "Missing-album sync: scanned {Scanned} owned artist(s), {Missing} missing album(s) total",
            scanned, missingTotal);
        return new MissingAlbumSyncResult(scanned, missingTotal);
    }

    /// <summary>
    /// Resolves one artist's Deezer discography, diffs it against the albums already owned for that
    /// artist, and persists the gap into <see cref="IMissingAlbumRepo"/> (replacing the artist's prior
    /// rows). Shared by the bulk <see cref="Refresh"/> sweep over owned artists and the on-demand
    /// expansion when a user likes a brand-new recommended artist (whose owned set is empty, so the
    /// whole discography surfaces as acquirable). Returns the persisted rows.
    /// </summary>
    public async Task<IReadOnlyList<MissingAlbum>> RefreshOne(ArtistKey artist, IEnumerable<string> ownedAlbumTitles)
    {
        var diff = await FetchAndDiff(artist, ownedAlbumTitles);
        var missing = diff?.Missing ?? new List<MissingAlbum>();
        // Persist the gap (or clear stale rows when the artist has no Deezer match) so the per-user
        // feed and a later like — which carries the row's DeezerAlbumId to the downloader — stay current.
        await _missing.ReplaceForArtist(artist.ArtistName, missing);
        return missing;
    }

    /// <summary>
    /// One artist's full Deezer discography (every LP), each flagged with whether the library already
    /// owns it — for the Artists-page drill-down. Persists the missing subset as a side effect (same as
    /// <see cref="RefreshOne"/>) so a later like carries the Deezer id to the downloader. Owned albums the
    /// library has that Deezer doesn't list as an LP are appended (without art/id) so the picture is
    /// complete.
    /// </summary>
    public async Task<IReadOnlyList<DiscographyAlbum>> Discography(ArtistKey artist, IReadOnlyCollection<string> ownedAlbumTitles)
    {
        var diff = await FetchAndDiff(artist, ownedAlbumTitles);
        await _missing.ReplaceForArtist(artist.ArtistName, diff?.Missing ?? new List<MissingAlbum>());

        var all = diff?.All.ToList() ?? new List<DiscographyAlbum>();
        // Fold in owned albums Deezer didn't surface as LPs (singles/comps it filtered, or no match)
        // so the library's view is the source of truth for what we have.
        var seen = all.Select(a => NormalizeTitle(a.Title)).ToHashSet(StringComparer.Ordinal);
        foreach (var title in ownedAlbumTitles)
        {
            if (seen.Add(NormalizeTitle(title)))
            {
                all.Add(new DiscographyAlbum(title, null, null, Owned: true));
            }
        }
        return all;
    }

    /// <summary>
    /// Resolves the artist on Deezer and walks its discography once, splitting it into the full
    /// annotated list (every LP, flagged owned/missing) and the missing subset. Returns null when the
    /// artist has no Deezer match. Compares on a normalized title so punctuation/casing differences
    /// between Plex and Deezer (e.g. a typographic vs. straight apostrophe) don't make an owned album
    /// look missing; the original Deezer title is still what we surface.
    /// </summary>
    private async Task<(List<DiscographyAlbum> All, List<MissingAlbum> Missing)?> FetchAndDiff(
        ArtistKey artist, IEnumerable<string> ownedAlbumTitles)
    {
        var deezerId = await _resolver.ResolveArtistId(artist.ArtistName);
        if (deezerId is null)
        {
            return null;
        }

        var owned = ownedAlbumTitles
            .Select(NormalizeTitle)
            .ToHashSet(StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = new List<DiscographyAlbum>();
        var missing = new List<MissingAlbum>();
        foreach (var album in await _deezer.GetAlbums(deezerId.Value))
        {
            var title = album.title;
            var key = NormalizeTitle(title);
            if (string.IsNullOrEmpty(key)
                || !string.Equals(album.record_type, AlbumRecordType, StringComparison.OrdinalIgnoreCase)
                || !seen.Add(key))
            {
                continue;
            }

            var isOwned = owned.Contains(key);
            all.Add(new DiscographyAlbum(title, album.BestCoverUrl, album.id, isOwned));
            if (!isOwned)
            {
                missing.Add(new MissingAlbum(artist, new AlbumKey(title), album.BestCoverUrl, album.id));
            }
        }

        return (all, missing);
    }

    private static string NormalizeTitle(string? title) => AlbumTitleMatcher.Normalize(title);
}
