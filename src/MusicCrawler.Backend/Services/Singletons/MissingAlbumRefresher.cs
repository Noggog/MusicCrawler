using System.Text;
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
        var name = artist.ArtistName;
        var deezerId = await _resolver.ResolveArtistId(name);
        if (deezerId is null)
        {
            // No Deezer match — clear any stale rows so we don't keep suggesting against nothing.
            await _missing.ReplaceForArtist(name, Array.Empty<MissingAlbum>());
            return Array.Empty<MissingAlbum>();
        }

        // Compare on a normalized form so punctuation/casing differences between Plex and Deezer
        // (e.g. a typographic vs. straight apostrophe in "so the flies don't come") don't make an
        // owned album look missing. The original Deezer title is still what we persist/display.
        var owned = ownedAlbumTitles
            .Select(NormalizeTitle)
            .ToHashSet(StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<MissingAlbum>();
        foreach (var album in await _deezer.GetAlbums(deezerId.Value))
        {
            var title = album.title;
            var key = NormalizeTitle(title);
            if (string.IsNullOrEmpty(key)
                || !string.Equals(album.record_type, AlbumRecordType, StringComparison.OrdinalIgnoreCase)
                || owned.Contains(key)
                || !seen.Add(key))
            {
                continue;
            }

            missing.Add(new MissingAlbum(artist, new AlbumKey(title), album.BestCoverUrl, album.id));
        }

        await _missing.ReplaceForArtist(name, missing);
        return missing;
    }

    /// <summary>
    /// Canonical form for matching album titles across sources: trimmed, lower-cased, with curly
    /// quotes/apostrophes and en/em dashes folded to ASCII and internal whitespace collapsed — so a
    /// title that differs only in typography (Plex's "Don't" vs. Deezer's "Don't") still matches.
    /// </summary>
    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(title.Length);
        var lastWasSpace = false;
        foreach (var ch in title.Trim())
        {
            var c = ch switch
            {
                '‘' or '’' or 'ʼ' or '′' => '\'', // curly/modifier apostrophes, prime
                '“' or '”' => '"',                          // curly double quotes
                '–' or '—' => '-',                          // en/em dash
                _ => char.ToLowerInvariant(ch),
            };

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
