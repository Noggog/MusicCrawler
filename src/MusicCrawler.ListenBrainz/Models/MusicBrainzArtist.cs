using Newtonsoft.Json;

namespace MusicCrawler.ListenBrainz.Models;

/// <summary>
/// One artist hit from a MusicBrainz search. <see cref="Id"/> is the MBID — the stable identifier
/// the ListenBrainz similarity endpoint is keyed by. <see cref="Score"/> is MusicBrainz's search
/// relevance (0-100); <see cref="Disambiguation"/> is the parenthetical that tells two same-named
/// acts apart (handy when surfacing a "wrong artist" correction later).
/// </summary>
public class MusicBrainzArtist
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("score")]
    public int Score { get; set; }

    [JsonProperty("disambiguation")]
    public string? Disambiguation { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }
}

/// <summary>Envelope of a MusicBrainz <c>/ws/2/artist</c> search: <c>{ "artists": [ ... ] }</c>.</summary>
public class MusicBrainzSearchResult
{
    [JsonProperty("artists")]
    public List<MusicBrainzArtist> Artists { get; set; } = new();
}
