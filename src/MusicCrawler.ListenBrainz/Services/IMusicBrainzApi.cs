using MusicCrawler.ListenBrainz.Models;

namespace MusicCrawler.ListenBrainz.Services;

/// <summary>
/// Thin client over MusicBrainz's keyless search API, used only to resolve an artist name to its
/// MBID (the id the ListenBrainz similarity endpoint needs). Degrades gracefully (returns null) on
/// a miss or transport error rather than throwing, and self-throttles to MusicBrainz's 1 req/s.
/// </summary>
public interface IMusicBrainzApi
{
    /// <summary>
    /// Resolve an artist name to its strongest MusicBrainz match (highest search score), or null if
    /// none/error. The returned <see cref="MusicBrainzArtist.Id"/> is the MBID.
    /// </summary>
    Task<MusicBrainzArtist?> SearchArtist(string artistName);

    /// <summary>
    /// Free-text artist search in relevance order (empty if none/error), powering the "Correct
    /// association" picker when the top hit is wrong.
    /// </summary>
    Task<MusicBrainzArtist[]> SearchArtists(string query, int limit);

    /// <summary>Look up a MusicBrainz artist by its MBID (name, disambiguation), or null if none/error.</summary>
    Task<MusicBrainzArtist?> GetArtist(string mbid);
}
