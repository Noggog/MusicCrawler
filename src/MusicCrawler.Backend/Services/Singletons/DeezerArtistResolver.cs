using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using MusicCrawler.Deezer.Services;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>One sampleable track: a title and its ~30s preview MP3.</summary>
public record DeezerPreviewTrack(string Title, string PreviewUrl);

/// <summary>What the SPA needs to sample an artist: a few 30-second previews and a link out.</summary>
/// <param name="Id">Deezer artist id.</param>
/// <param name="ArtistLink">Canonical deezer.com artist page (the "open wholesale" link).</param>
/// <param name="ImageUrl">Deezer's artist photo (largest available), or null if it supplied none.</param>
/// <param name="Tracks">The artist's top previewable tracks (biggest first), possibly empty.</param>
public record DeezerPlayInfo(long Id, string ArtistLink, string? ImageUrl, IReadOnlyList<DeezerPreviewTrack> Tracks);

/// <summary>What the SPA needs to sample a specific album: its previewable tracks and a link out.</summary>
/// <param name="Id">Deezer album id.</param>
/// <param name="AlbumLink">Canonical deezer.com album page.</param>
/// <param name="Tracks">The album's previewable tracks (track order), possibly empty.</param>
public record DeezerAlbumPlayInfo(long Id, string AlbumLink, IReadOnlyList<DeezerPreviewTrack> Tracks);

/// <summary>
/// Resolves an artist name to Deezer playback info: the artist id, its page link, and a 30-second
/// preview MP3 from the artist's top track. A plain &lt;audio&gt; element plays that preview with no
/// login/cookies/iframe (unlike the embed widget). Results are cached so showing a recommendation
/// card never re-hits Deezer for something already looked up.
/// </summary>
public class DeezerArtistResolver
{
    /// <summary>How many top tracks to offer as previews.</summary>
    private const int TopTrackCount = 5;

    // The artist id is immutable; cache it long. Preview URLs can rotate, so cache them briefly.
    private static readonly DistributedCacheEntryOptions IdCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
    };

    private static readonly DistributedCacheEntryOptions TopTracksCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12),
    };

    private readonly IDeezerApi _deezer;
    private readonly IDistributedCache _cache;

    public DeezerArtistResolver(IDeezerApi deezer, IDistributedCache cache)
    {
        _deezer = deezer;
        _cache = cache;
    }

    /// <summary>Full sample/link/image info for an artist name, or null if Deezer has no match.</summary>
    public async Task<DeezerPlayInfo?> ResolvePlayInfo(string artistName)
    {
        var artist = await ResolveArtist(artistName);
        if (artist is null)
        {
            return null;
        }

        var tracks = await ResolveTopTracks(artist.Id);
        return new DeezerPlayInfo(
            artist.Id,
            $"https://www.deezer.com/artist/{artist.Id}",
            artist.ImageUrl,
            tracks);
    }

    /// <summary>The Deezer artist id for a name, or null if Deezer has no match. Cached.</summary>
    public async Task<long?> ResolveArtistId(string artistName) =>
        (await ResolveArtist(artistName))?.Id;

    /// <summary>The Deezer id + photo for an artist name (or null on no match). Cached as JSON so the
    /// id and image come from one search — the "no match" case is an empty-string sentinel.</summary>
    private async Task<CachedArtist?> ResolveArtist(string artistName)
    {
        var key = $"deezer:artist:{artistName.ToLowerInvariant()}";

        var cached = await _cache.GetStringAsync(key);
        if (cached != null)
        {
            return cached.Length == 0 ? null : JsonSerializer.Deserialize<CachedArtist>(cached);
        }

        var artist = await _deezer.SearchArtist(artistName);
        var resolved = artist is null ? null : new CachedArtist(artist.id, artist.BestImageUrl);
        await _cache.SetStringAsync(
            key,
            resolved is null ? "" : JsonSerializer.Serialize(resolved),
            IdCacheOptions);
        return resolved;
    }

    /// <summary>The cached identity of a Deezer artist: its id and best photo URL.</summary>
    private record CachedArtist(long Id, string? ImageUrl);

    /// <summary>
    /// Sample/link info for a specific Deezer album id. The album id is already known (it comes from
    /// the missing-album record), so this never misses — the link is always valid; the track list may
    /// be empty if Deezer has no previews. Previews are cached briefly since they can rotate.
    /// </summary>
    public async Task<DeezerAlbumPlayInfo> ResolveAlbumPlayInfo(long albumId)
    {
        var tracks = await ResolveAlbumTracks(albumId);
        return new DeezerAlbumPlayInfo(albumId, $"https://www.deezer.com/album/{albumId}", tracks);
    }

    private async Task<IReadOnlyList<DeezerPreviewTrack>> ResolveAlbumTracks(long albumId)
    {
        var key = $"deezer:albumtracks:{albumId}";

        var cached = await _cache.GetStringAsync(key);
        if (cached != null)
        {
            return cached.Length == 0
                ? Array.Empty<DeezerPreviewTrack>()
                : JsonSerializer.Deserialize<DeezerPreviewTrack[]>(cached) ?? Array.Empty<DeezerPreviewTrack>();
        }

        var tracks = (await _deezer.GetAlbumTracks(albumId))
            .Where(t => !string.IsNullOrEmpty(t.preview) && !string.IsNullOrEmpty(t.title))
            .Select(t => new DeezerPreviewTrack(t.title!, t.preview!))
            .ToArray();

        await _cache.SetStringAsync(
            key,
            tracks.Length == 0 ? "" : JsonSerializer.Serialize(tracks),
            TopTracksCacheOptions);
        return tracks;
    }

    private async Task<IReadOnlyList<DeezerPreviewTrack>> ResolveTopTracks(long artistId)
    {
        var key = $"deezer:toptracks:{artistId}";

        var cached = await _cache.GetStringAsync(key);
        if (cached != null)
        {
            return cached.Length == 0
                ? Array.Empty<DeezerPreviewTrack>()
                : JsonSerializer.Deserialize<DeezerPreviewTrack[]>(cached) ?? Array.Empty<DeezerPreviewTrack>();
        }

        var tracks = (await _deezer.GetTopTracks(artistId, TopTrackCount))
            .Where(t => !string.IsNullOrEmpty(t.preview) && !string.IsNullOrEmpty(t.title))
            .Select(t => new DeezerPreviewTrack(t.title!, t.preview!))
            .ToArray();

        await _cache.SetStringAsync(
            key,
            tracks.Length == 0 ? "" : JsonSerializer.Serialize(tracks),
            TopTracksCacheOptions);
        return tracks;
    }
}
