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
        // Deezer orders search results by relevance; the first is the strongest match.
        return (await SearchArtists(artistName, 1)).FirstOrDefault();
    }

    public async Task<DeezerArtist[]> SearchArtists(string query, int limit)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["q"] = query;
        qs["limit"] = limit.ToString();
        var url = $"{_endpointInfo.BaseUri}/search/artist?{qs}";

        var result = await Get<DeezerArtistList>(url);
        return result?.data.ToArray() ?? Array.Empty<DeezerArtist>();
    }

    public async Task<DeezerArtist?> GetArtist(long artistId)
    {
        var url = $"{_endpointInfo.BaseUri}/artist/{artistId}";
        return await Get<DeezerArtist>(url);
    }

    public async Task<DeezerArtist[]> GetRelated(long artistId)
    {
        var url = $"{_endpointInfo.BaseUri}/artist/{artistId}/related";
        var result = await Get<DeezerArtistList>(url);
        return result?.data.ToArray() ?? Array.Empty<DeezerArtist>();
    }

    public async Task<DeezerTrack[]> GetTopTracks(long artistId, int limit)
    {
        // Deezer orders /top by popularity, so these come back biggest-first.
        var url = $"{_endpointInfo.BaseUri}/artist/{artistId}/top?limit={limit}";
        var result = await Get<DeezerTrackList>(url);
        return result?.data.ToArray() ?? Array.Empty<DeezerTrack>();
    }

    public async Task<DeezerAlbum[]> GetAlbums(long artistId)
    {
        // limit=300 comfortably covers any discography in one page (Deezer's default page is 25).
        var url = $"{_endpointInfo.BaseUri}/artist/{artistId}/albums?limit=300";
        var result = await Get<DeezerAlbumList>(url);
        return result?.data.ToArray() ?? Array.Empty<DeezerAlbum>();
    }

    public async Task<DeezerTrack[]> GetAlbumTracks(long albumId)
    {
        var url = $"{_endpointInfo.BaseUri}/album/{albumId}/tracks";
        var result = await Get<DeezerTrackList>(url);
        return result?.data.ToArray() ?? Array.Empty<DeezerTrack>();
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
