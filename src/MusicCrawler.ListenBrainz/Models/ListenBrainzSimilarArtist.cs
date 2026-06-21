using Newtonsoft.Json;

namespace MusicCrawler.ListenBrainz.Models;

/// <summary>
/// One similar artist from the ListenBrainz labs similar-artists endpoint (the response is a bare
/// JSON array of these). Carries both the <see cref="ArtistMbid"/> and the canonical
/// <see cref="Name"/>, so results drop straight into the name-keyed similarity graph without a
/// reverse MBID-&gt;name lookup. <see cref="Score"/> is a raw co-occurrence count (not 0-1), so it's
/// only comparable within one seed's result set — normalize before blending across sources.
/// </summary>
public class ListenBrainzSimilarArtist
{
    [JsonProperty("artist_mbid")]
    public string? ArtistMbid { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("score")]
    public double Score { get; set; }

    [JsonProperty("comment")]
    public string? Comment { get; set; }
}
