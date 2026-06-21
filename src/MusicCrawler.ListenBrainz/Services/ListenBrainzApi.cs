using System.Web;
using Microsoft.Extensions.Logging;
using MusicCrawler.ListenBrainz.Inputs;
using MusicCrawler.ListenBrainz.Models;
using Newtonsoft.Json;

namespace MusicCrawler.ListenBrainz.Services;

/// <summary>
/// docs: https://labs.api.listenbrainz.org  (keyless)
///   GET /similar-artists/json?artist_mbids={mbid}&amp;algorithm={algo} -> [ {artist_mbid, name, score}, ... ]
///
/// Mirrors <c>DeezerApi</c>'s resilience: returns empty on any transport/parse failure so a flaky
/// labs service never takes ingestion down. The response is a bare JSON array (no envelope).
/// </summary>
public class ListenBrainzApi : IListenBrainzApi
{
    private readonly HttpClient _httpClient;
    private readonly ListenBrainzEndpointInfo _endpointInfo;
    private readonly ILogger<ListenBrainzApi> _logger;

    public ListenBrainzApi(ListenBrainzEndpointInfo endpointInfo, ILogger<ListenBrainzApi> logger)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(endpointInfo.UserAgent);
        _endpointInfo = endpointInfo;
        _logger = logger;
    }

    public async Task<ListenBrainzSimilarArtist[]> GetSimilarArtists(string artistMbid)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["artist_mbids"] = artistMbid;
        qs["algorithm"] = _endpointInfo.Algorithm;
        var url = $"{_endpointInfo.ListenBrainzBaseUri}/similar-artists/json?{qs}";

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ListenBrainz request failed: {Status} for {Url}", response.StatusCode, url);
                return Array.Empty<ListenBrainzSimilarArtist>();
            }

            var body = await response.Content.ReadAsStringAsync();
            // The seed artist itself is included in the array; callers filter it out by MBID.
            return JsonConvert.DeserializeObject<ListenBrainzSimilarArtist[]>(body)
                   ?? Array.Empty<ListenBrainzSimilarArtist>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ListenBrainz request errored for {Url}", url);
            return Array.Empty<ListenBrainzSimilarArtist>();
        }
    }
}
