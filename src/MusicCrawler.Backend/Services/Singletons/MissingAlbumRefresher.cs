using MusicCrawler.Deezer.Services;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>The outcome of one missing-album sync pass.</summary>
public record MissingAlbumSyncResult(int ArtistsScanned, int MissingTotal);

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
            var name = artist.ArtistKey.ArtistName;
            scanned++;

            var deezerId = await _resolver.ResolveArtistId(name);
            if (deezerId is null)
            {
                // No Deezer match — clear any stale rows so we don't keep suggesting against nothing.
                await _missing.ReplaceForArtist(name, Array.Empty<MissingAlbum>());
                continue;
            }

            var owned = ownedAlbums.TryGetValue(name, out var set)
                ? set
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<MissingAlbum>();
            foreach (var album in await _deezer.GetAlbums(deezerId.Value))
            {
                var title = album.title;
                if (string.IsNullOrWhiteSpace(title)
                    || !string.Equals(album.record_type, AlbumRecordType, StringComparison.OrdinalIgnoreCase)
                    || owned.Contains(title)
                    || !seen.Add(title))
                {
                    continue;
                }

                missing.Add(new MissingAlbum(artist.ArtistKey, new AlbumKey(title), album.BestCoverUrl));
            }

            await _missing.ReplaceForArtist(name, missing);
            missingTotal += missing.Count;
        }

        _logger.LogInformation(
            "Missing-album sync: scanned {Scanned} owned artist(s), {Missing} missing album(s) total",
            scanned, missingTotal);
        return new MissingAlbumSyncResult(scanned, missingTotal);
    }
}
