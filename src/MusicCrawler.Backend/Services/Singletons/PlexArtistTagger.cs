using MusicCrawler.Interfaces;
using MusicCrawler.Plex.Services;
using MusicCrawler.Plex.Services.Singletons;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// <see cref="IArtistTagger"/> over Plex. Stamps a user's like/dislike onto the artist in Plex as a
/// Collection membership (e.g. "noggog_liked"), so a taste verdict made in the app is visible in Plex
/// and — unlike a Label — filterable by a music smart playlist via the "Artist Collection" field.
///
/// <para><b>Reconciling, one pass.</b> Plex's metadata edit replaces the whole Collection field, so we
/// read the artist's current collections and PUT them back with the keeper added and the drops removed —
/// preserving genres (a separate field) and any other user's tags. Doing the add and remove in the same
/// scan means a rating (which both stamps the new verdict and strips the opposite) costs one read, not
/// two. A name can map to more than one Plex item (Plex joins collaborators into a single ';'-delimited
/// title), so every item the name appears in is updated, matching how the rest of the app reads names.</para>
///
/// <para><b>Targeted, with a scan fallback.</b> The catalog stores each artist's Plex rating key(s)
/// (captured on every refresh), so the hot path reads those keys and fetches just those items instead
/// of pulling the whole ~1800-artist library. When no keys are stored (cold cache / artist not in the
/// catalog) or a stored key has gone stale (returns no item), it falls back to the legacy name scan,
/// which the next catalog refresh repairs.</para>
///
/// <para><b>Best-effort.</b> Failures are logged, never thrown — tagging is a side effect of rating and
/// must not fail the rating itself.</para>
/// </summary>
public class PlexArtistTagger : IArtistTagger
{
    private readonly IPlexApi _plexApi;
    private readonly IArtistCatalogRepo _catalog;
    private readonly ILogger<PlexArtistTagger> _logger;

    public PlexArtistTagger(IPlexApi plexApi, IArtistCatalogRepo catalog, ILogger<PlexArtistTagger> logger)
    {
        _plexApi = plexApi;
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>
    /// Computes the collection set to PUT for one Plex item: drops every tag in <paramref name="remove"/>
    /// (case-insensitively) and ensures <paramref name="add"/> is present, leaving all other collections
    /// untouched. Returns <c>null</c> when the item is already in the desired state (nothing to write).
    /// </summary>
    internal static IReadOnlyList<string>? ReconcileCollections(
        string[] existing, string? add, IReadOnlyCollection<string> remove)
    {
        var removedAny = existing.Any(l => remove.Contains(l, StringComparer.OrdinalIgnoreCase));
        var needAdd = add != null && !existing.Contains(add, StringComparer.OrdinalIgnoreCase);
        if (!removedAny && !needAdd)
        {
            return null;
        }

        var next = existing.Where(l => !remove.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList();
        if (needAdd)
        {
            next.Add(add!);
        }

        return next;
    }

    public async Task SetTags(string artistName, string? add, IReadOnlyCollection<string> remove)
    {
        var addTag = string.IsNullOrWhiteSpace(add) ? null : add;
        if (string.IsNullOrWhiteSpace(artistName) || (addTag == null && remove.Count == 0))
        {
            return;
        }

        try
        {
            var keys = await _catalog.GetPlexRatingKeys(new ArtistKey(artistName));
            if (keys.Count == 0)
            {
                // Cold cache, or an artist not in the catalog (e.g. a thumbed not-in-library related
                // artist) — fall back to the name scan, exactly as before the optimization.
                await SetTagsByScan(artistName, addTag, remove);
                return;
            }

            var library = await _plexApi.ResolveLibrary();
            foreach (var key in keys)
            {
                var item = await _plexApi.GetMusicArtist(key);
                if (item == null)
                {
                    // Stale key (library rebuild, remove+re-add). Repair this op via the scan; the next
                    // catalog refresh rewrites the correct keys.
                    _logger.LogInformation(
                        "Stored Plex key {Key} for {Artist} no longer resolves; falling back to scan",
                        key, artistName);
                    await SetTagsByScan(artistName, addTag, remove);
                    return;
                }

                await ApplyCollections(library.Key, item, addTag, remove);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update Plex collections on {Artist}", artistName);
        }
    }

    /// <summary>
    /// The legacy path: pull the whole library, name-match, and reconcile. Kept as the fallback for
    /// cold cache / stale keys / artists absent from the catalog.
    /// </summary>
    private async Task SetTagsByScan(string artistName, string? addTag, IReadOnlyCollection<string> remove)
    {
        var library = await _plexApi.ResolveLibrary();
        var matches = (await _plexApi.GetMusicArtists(library.Key))
            .Where(a => ArtistNames.Split(a.Title)
                .Any(n => string.Equals(n, artistName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matches.Length == 0)
        {
            _logger.LogInformation("No Plex artist matched {Artist}; skipped tag update", artistName);
            return;
        }

        foreach (var artist in matches)
        {
            await ApplyCollections(library.Key, artist, addTag, remove);
        }
    }

    private async Task ApplyCollections(
        int libraryKey, PlexMusicArtist artist, string? addTag, IReadOnlyCollection<string> remove)
    {
        var existing = artist.Collections();
        var next = ReconcileCollections(existing, addTag, remove);
        if (next == null)
        {
            return; // already in the desired state on this item
        }

        // Plex tag edits are add-only unless removals are spelled out explicitly, so send the delta. Drops
        // carry the casing Plex actually stores, so a stale tag whose case differs from the app's generated
        // form (e.g. "Noggog_liked" vs "noggog_liked") still matches and is removed.
        var toAdd = next.Where(c => !existing.Contains(c, StringComparer.OrdinalIgnoreCase)).ToArray();
        var toRemove = existing.Where(c => !next.Contains(c, StringComparer.OrdinalIgnoreCase)).ToArray();

        await _plexApi.SetArtistCollections(libraryKey, artist.RatingKey, toAdd, toRemove);
        _logger.LogInformation(
            "Updated Plex collections on {Artist} ({Key}): +[{Add}] -[{Remove}]",
            artist.Title, artist.RatingKey, string.Join(", ", toAdd), string.Join(", ", toRemove));
    }
}
