using Microsoft.Extensions.Caching.Distributed;
using MusicCrawler.Interfaces;
using MusicCrawler.ListenBrainz.Models;
using MusicCrawler.ListenBrainz.Services;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Resolves an artist name to its MusicBrainz MBID — the id the ListenBrainz similarity endpoint is
/// keyed by. The counterpart to <see cref="DeezerArtistResolver"/> for the MetaBrainz source: honors
/// a user pin (override) stored on the catalog, otherwise takes MusicBrainz's top name-search hit,
/// and captures the MBID onto the catalog so the Artists-page "Sources" tab can show + correct it.
///
/// Caching matters more here than for Deezer: MusicBrainz is capped at ~1 req/s, so a cold pass over
/// a large library is slow. The MBID is immutable, so resolutions cache for 30 days; a confirmed
/// no-match caches as an empty string so a missing artist isn't re-searched every read.
/// </summary>
public class MusicBrainzArtistResolver
{
    private static readonly DistributedCacheEntryOptions IdCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
    };

    private readonly IMusicBrainzApi _musicBrainz;
    private readonly IDistributedCache _cache;
    private readonly IArtistCatalogRepo _catalog;

    public MusicBrainzArtistResolver(IMusicBrainzApi musicBrainz, IDistributedCache cache, IArtistCatalogRepo catalog)
    {
        _musicBrainz = musicBrainz;
        _cache = cache;
        _catalog = catalog;
    }

    /// <summary>
    /// The MusicBrainz artist a name resolves to (MBID, name, disambiguation), or null on no match /
    /// unreachable. Honors a user override pinned on the catalog (resolved by MBID, never re-searched);
    /// otherwise takes MusicBrainz's top name-search hit. Every successful resolution is captured onto
    /// the catalog, and cached so repeated reads never re-hit the rate-limited search API.
    /// </summary>
    public async Task<MusicBrainzIdentity?> ResolveIdentity(string artistName)
    {
        // A user pin wins outright — the whole point is to stop guessing by name.
        var stored = await _catalog.GetMusicBrainz(new ArtistKey(artistName));
        if (stored is { IsOverride: true })
        {
            return stored.Value.Identity;
        }

        var key = NameCacheKey(artistName);
        MusicBrainzIdentity? identity;

        var cached = await _cache.GetStringAsync(key);
        if (cached != null)
        {
            identity = cached.Length == 0 ? null : Deserialize(cached);
        }
        else
        {
            identity = ToIdentity(await _musicBrainz.SearchArtist(artistName));
            // A transport error also returns null; only cache an actual answer (hit or empty result),
            // so a transient failure doesn't suppress retries for 30 days.
            await _cache.SetStringAsync(key, identity is null ? "" : Serialize(identity), IdCacheOptions);
        }

        // Opportunistic capture onto the catalog (for the Sources tab). Done on cache hits too, not
        // just misses — otherwise a warm cache (Redis) means nothing ever lands in the catalog. Skip
        // when the catalog already has this MBID, and never overturn an override (the repo guards).
        if (identity != null && stored?.Identity.Mbid != identity.Mbid)
        {
            await _catalog.SetMusicBrainzIdentity(new ArtistKey(artistName), identity, isOverride: false);
        }

        return identity;
    }

    /// <summary>
    /// Pins an artist to a specific MBID (a user correction). Looks that artist up by id, persists it
    /// as a sticky override, and evicts any stale name-resolution. Returns the pinned identity, or
    /// null if MusicBrainz has no artist with that MBID.
    /// </summary>
    public async Task<MusicBrainzIdentity?> SetOverride(string artistName, string mbid)
    {
        var identity = ToIdentity(await _musicBrainz.GetArtist(mbid));
        if (identity is null)
        {
            return null;
        }

        await _catalog.SetMusicBrainzIdentity(new ArtistKey(artistName), identity, isOverride: true);
        await _cache.RemoveAsync(NameCacheKey(artistName));
        return identity;
    }

    /// <summary>Clears a user pin so the artist re-resolves from a name search next time.</summary>
    public async Task ClearOverride(string artistName)
    {
        await _catalog.ClearMusicBrainzOverride(new ArtistKey(artistName));
        await _cache.RemoveAsync(NameCacheKey(artistName));
    }

    /// <summary>Free-text MusicBrainz artist search for the "Correct association" picker.</summary>
    public async Task<IReadOnlyList<MusicBrainzIdentity>> SearchArtists(string query, int limit) =>
        (await _musicBrainz.SearchArtists(query, limit))
            .Select(ToIdentity)
            .Where(i => i != null)
            .Select(i => i!)
            .ToArray();

    private static string NameCacheKey(string artistName) =>
        $"musicbrainz:artist:v1:{artistName.ToLowerInvariant()}";

    private static MusicBrainzIdentity? ToIdentity(MusicBrainzArtist? artist) =>
        artist?.Id is { Length: > 0 }
            ? new MusicBrainzIdentity(artist.Id, artist.Name, artist.Disambiguation)
            : null;

    // The cached value is "mbid\tname\tdisambiguation"; a plain join avoids pulling in a serializer
    // for three fields (an MBID never contains a tab).
    private static string Serialize(MusicBrainzIdentity id) => $"{id.Mbid}\t{id.Name}\t{id.Disambiguation}";

    private static MusicBrainzIdentity Deserialize(string cached)
    {
        var parts = cached.Split('\t');
        return new MusicBrainzIdentity(
            parts[0],
            parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null,
            parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null);
    }
}
