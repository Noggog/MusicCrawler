namespace MusicCrawler.ListenBrainz.Inputs;

/// <summary>
/// Connection settings for the open MetaBrainz stack this source spans two services of:
///   * <paramref name="MusicBrainzBaseUri"/> — name -> MBID resolution (/ws/2/artist search).
///   * <paramref name="ListenBrainzBaseUri"/> — MBID -> similar artists (labs similar-artists/json).
/// Both are keyless. MusicBrainz *requires* a descriptive User-Agent with contact info or it
/// throttles hard, hence <paramref name="Contact"/>. <paramref name="Algorithm"/> is the labs
/// tuning string (session window, limit, ...); <paramref name="Enabled"/> is the off-switch so the
/// source can be disabled without touching code (it then never hits the network).
/// </summary>
public record ListenBrainzEndpointInfo(
    string MusicBrainzBaseUri,
    string ListenBrainzBaseUri,
    string Contact,
    string Algorithm,
    bool Enabled)
{
    /// <summary>
    /// User-Agent sent to both services. MusicBrainz's etiquette asks for "App/version ( contact )"
    /// so a maintainer is reachable; we send the same string to ListenBrainz to be a good citizen.
    /// </summary>
    public string UserAgent => $"MusicCrawler/1.0 ( {Contact} )";
}
