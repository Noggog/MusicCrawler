using MusicCrawler.Deezer.Models;

namespace MusicCrawler.Deezer.Services;

/// <summary>
/// Thin client over the keyless Deezer public API. Every call degrades gracefully (returns
/// null / empty) on a miss or transport error rather than throwing, so ingestion can keep
/// going when Deezer is flaky.
/// </summary>
public interface IDeezerApi
{
    /// <summary>Resolve an artist name to its Deezer artist (strongest match), or null if none.</summary>
    Task<DeezerArtist?> SearchArtist(string artistName);

    /// <summary>Deezer's "related artists" for the given artist id (empty if none/error).</summary>
    Task<DeezerArtist[]> GetRelated(long artistId);

    /// <summary>
    /// The artist's most popular tracks (for their ~30s preview URLs), most popular first.
    /// Empty if none/error.
    /// </summary>
    Task<DeezerTrack[]> GetTopTracks(long artistId, int limit);

    /// <summary>The artist's albums (their discography). Empty if none/error.</summary>
    Task<DeezerAlbum[]> GetAlbums(long artistId);

    /// <summary>An album's tracks (for their ~30s preview URLs), in track order. Empty if none/error.</summary>
    Task<DeezerTrack[]> GetAlbumTracks(long albumId);
}
