using System.Web;
using Microsoft.Extensions.Logging;
using MusicCrawler.Deezer.Inputs;
using MusicCrawler.Deezer.Models;
using Newtonsoft.Json;

namespace MusicCrawler.Deezer.Services;

/// <summary>
/// docs: https://developers.deezer.com/api  (keyless, no auth required)
///   search:  GET /search/artist?q={name}   -> { data: [ {id, name, picture_*}, ... ] }
///   related: GET /artist/{id}/related       -> { data: [ {id, name, picture_*}, ... ] }
///
/// Mirrors the existing SpotifyApi/PlexApi convention: own HttpClient, Newtonsoft deserialization,
/// injected ILogger. Unlike those, this guards transport/parse failures and returns empty so a
/// flaky Deezer never takes ingestion down — resilience is the whole point of the persisted graph.
/// </summary>
public class DeezerApi : IDeezerApi
{
    private readonly HttpClient _httpClient;
    private readonly DeezerEndpointInfo _endpointInfo;
    private readonly ILogger<DeezerApi> _logger;

    public DeezerApi(DeezerEndpointInfo endpointInfo, ILogger<DeezerApi> logger)
    {
        _httpClient = new HttpClient();
        _endpointInfo = endpointInfo;
        _logger = logger;
    }

    public async Task<DeezerArtist?> SearchArtist(string artistName)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["q"] = artistName;
        var url = $"{_endpointInfo.BaseUri}/search/artist?{query}";

        var result = await Get<DeezerArtistList>(url);
        // Deezer orders search results by relevance; the first is the strongest match.
        return result?.data.FirstOrDefault();
    }

    public async Task<DeezerArtist[]> GetRelated(long artistId)
    {
        var url = $"{_endpointInfo.BaseUri}/artist/{artistId}/related";
        var result = await Get<DeezerArtistList>(url);
        return result?.data.ToArray() ?? Array.Empty<DeezerArtist>();
    }

    private async Task<T?> Get<T>(string url) where T : class
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Deezer request failed: {Status} for {Url}", response.StatusCode, url);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deezer request errored for {Url}", url);
            return null;
        }
    }
}
