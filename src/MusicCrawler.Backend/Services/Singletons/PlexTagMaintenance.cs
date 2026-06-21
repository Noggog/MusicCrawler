using MusicCrawler.Interfaces;
using MusicCrawler.Plex.Services;
using MusicCrawler.Plex.Services.Singletons;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Dev/maintenance for the per-user Plex like/dislike collections written by <see cref="PlexArtistTagger"/>.
/// Lets us wipe the managed tags back to a clean slate and rebuild them from the stored ratings, so
/// iterating on the tagging logic doesn't leave orphaned tags scattered across the Plex library.
///
/// <para>"Managed" tags are exactly those collections with the "_liked"/"_disliked" suffix (see
/// <see cref="ArtistTag.IsManaged"/>); every other collection — hand-made groupings and other apps' tags —
/// is left alone. Current collections are read from the section listing (Plex returns them inline when
/// includeCollections=1 is set), and each write merges so a PUT never drops collections it didn't intend to.</para>
/// </summary>
public class PlexTagMaintenance
{
    private readonly PlexApi _plexApi;
    private readonly IUserQueueRepo _queue;
    private readonly IUserRepo _users;
    private readonly ILogger<PlexTagMaintenance> _logger;

    public PlexTagMaintenance(
        PlexApi plexApi, IUserQueueRepo queue, IUserRepo users, ILogger<PlexTagMaintenance> logger)
    {
        _plexApi = plexApi;
        _queue = queue;
        _users = users;
        _logger = logger;
    }

    /// <summary>The outcome of a <see cref="Rebuild"/>: artists wiped, then (artist, tag) pairs applied.</summary>
    public readonly record struct RebuildResult(int Cleared, int Applied);

    /// <summary>Wipe then reapply — the canonical "nuke the tags and rebuild from ratings" operation.</summary>
    public async Task<RebuildResult> Rebuild()
    {
        var cleared = await ClearManagedTags();
        var applied = await ReapplyFromRatings();
        return new RebuildResult(cleared, applied);
    }

    /// <summary>
    /// Strips every managed ("_liked"/"_disliked") collection from every artist in the library, preserving
    /// all other collections. Returns the number of artists changed.
    /// </summary>
    public async Task<int> ClearManagedTags()
    {
        var library = await _plexApi.ResolveLibrary();
        var changed = 0;
        foreach (var artist in await _plexApi.GetMusicArtists(library.Key))
        {
            var current = artist.Collections();
            var managed = current.Where(ArtistTag.IsManaged).ToArray();
            if (managed.Length == 0)
            {
                continue; // nothing managed on this artist
            }

            // Plex only drops tags via an explicit removal, so strip the managed ones by name (their
            // stored casing), leaving every other collection untouched.
            await _plexApi.SetArtistCollections(library.Key, artist.RatingKey, Array.Empty<string>(), managed);
            changed++;
        }

        _logger.LogInformation("Cleared managed Plex tags from {Count} artist(s)", changed);
        return changed;
    }

    /// <summary>
    /// Reapplies like/dislike collections from the stored ratings of every user that has any. The tag
    /// prefix comes from each user's stored username (the same source the live rating path uses); users
    /// with no usable username are skipped. Tags are accumulated per artist and merged with the artist's
    /// current collections, so one PUT per artist carries every applicable tag (and an already-present tag
    /// is a no-op). Returns the number of (artist, tag) applications.
    /// </summary>
    public async Task<int> ReapplyFromRatings()
    {
        var library = await _plexApi.ResolveLibrary();
        var artists = await _plexApi.GetMusicArtists(library.Key);
        var byName = BuildNameIndex(artists);

        // ratingKey -> the managed collection tags that should be present on that artist.
        var wanted = new Dictionary<int, HashSet<string>>();
        var applied = 0;

        foreach (var userId in await _queue.GetAllUserIds())
        {
            var user = await _users.Get(userId);
            foreach (var rating in await _queue.GetRated(userId))
            {
                var tag = ArtistTag.For(user?.Username, rating.Status);
                if (tag == null || !byName.TryGetValue(rating.Artist.ArtistName, out var items))
                {
                    continue;
                }

                foreach (var item in items)
                {
                    if (!wanted.TryGetValue(item.RatingKey, out var tags))
                    {
                        wanted[item.RatingKey] = tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }

                    if (tags.Add(tag))
                    {
                        applied++;
                    }
                }
            }
        }

        var byKey = artists.ToDictionary(a => a.RatingKey);
        foreach (var (ratingKey, tags) in wanted)
        {
            var current = byKey[ratingKey].Collections();
            var toAdd = tags.Where(t => !current.Contains(t, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (toAdd.Length > 0)
            {
                await _plexApi.SetArtistCollections(library.Key, ratingKey, toAdd, Array.Empty<string>());
            }
        }

        _logger.LogInformation(
            "Reapplied {Applied} Plex collection tag(s) across {Artists} artist(s)", applied, wanted.Count);
        return applied;
    }

    /// <summary>Indexes Plex artist items by each name encoded in their (possibly ';'-joined) title.</summary>
    private static Dictionary<string, List<PlexMusicArtist>> BuildNameIndex(PlexMusicArtist[] artists)
    {
        var index = new Dictionary<string, List<PlexMusicArtist>>(StringComparer.OrdinalIgnoreCase);
        foreach (var artist in artists)
        {
            foreach (var name in ArtistNames.Split(artist.Title))
            {
                if (!index.TryGetValue(name, out var list))
                {
                    index[name] = list = new List<PlexMusicArtist>();
                }
                list.Add(artist);
            }
        }
        return index;
    }
}
