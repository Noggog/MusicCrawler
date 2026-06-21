using MusicCrawler.ListenBrainz.Models;

namespace MusicCrawler.ListenBrainz.Services;

/// <summary>
/// Thin client over the keyless ListenBrainz labs similar-artists endpoint. Degrades gracefully
/// (returns empty) on a miss or transport error rather than throwing, so ingestion keeps going
/// when the service is flaky.
/// </summary>
public interface IListenBrainzApi
{
    /// <summary>
    /// The similar artists ListenBrainz reports for the given artist MBID, strongest first (empty
    /// if none/error). Each carries both an MBID and the canonical artist name.
    /// </summary>
    Task<ListenBrainzSimilarArtist[]> GetSimilarArtists(string artistMbid);
}
