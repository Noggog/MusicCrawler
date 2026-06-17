using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using MusicCrawler.Deezer.Services;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>One sampleable track: a title and its ~30s preview MP3.</summary>
public record DeezerPreviewTrack(string Title, string PreviewUrl);

/// <summary>What the SPA needs to sample an artist: a few 30-second previews and a link out.</summary>
/// <param name="Id">Deezer artist id.</param>
/// <param name="ArtistLink">Canonical deezer.com artist page (the "open wholesale" link).</param>
/// <param name="Tracks">The artist's top previewable tracks (biggest first), possibly empty.</param>
public record DeezerPlayInfo(long Id, string ArtistLink, IReadOnlyList<DeezerPreviewTrack> Tracks);

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

    /// <summary>Full sample/link info for an artist name, or null if Deezer has no match.</summary>
    public async Task<DeezerPlayInfo?> ResolvePlayInfo(string artistName)
    {
        var id = await ResolveArtistId(artistName);
        if (id is null)
        {
            return null;
        }

        var tracks = await ResolveTopTracks(id.Value);
        return new DeezerPlayInfo(
            id.Value,
            $"https://www.deezer.com/artist/{id.Value}",
            tracks);
    }

    /// <summary>The Deezer artist id for a name, or null if Deezer has no match. Cached.</summary>
    public async Task<long?> ResolveArtistId(string artistName)
    {
        var key = $"deezer:artistid:{artistName.ToLowerInvariant()}";

        var cached = await _cache.GetStringAsync(key);
        if (cached != null)
        {
            // Empty string is the cached "no match" sentinel — avoids re-searching unknown names.
            return cached.Length == 0 ? null : long.Parse(cached);
        }

        var artist = await _deezer.SearchArtist(artistName);
        await _cache.SetStringAsync(key, artist?.id.ToString() ?? "", IdCacheOptions);
        return artist?.id;
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
