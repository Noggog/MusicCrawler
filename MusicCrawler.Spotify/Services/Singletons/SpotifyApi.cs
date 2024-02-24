using System.Text;
using System.Web;
using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;
using MusicCrawler.Spotify.Inputs;
using MusicCrawler.Spotify.Models;
using Newtonsoft.Json;

namespace MusicCrawler.Spotify.Services.Singletons;

/**
 * docs:
 *      https://developer.spotify.com/documentation/web-api/reference/get-recommendations
 *      https://developer.spotify.com/documentation/web-api/concepts/authorization
 *      https://developer.spotify.com/documentation/web-api/tutorials/code-flow
 *      https://developer.spotify.com/documentation/web-api/tutorials/client-credentials-flow
 */
public class SpotifyApi
{
    private readonly HttpClient _httpClient;
    private readonly RandomStringGenerator _randomStringGenerator;
    private readonly SpotifyClientInfo _clientInfo;
    private readonly SpotifyEndpointInfo _endpointInfo;

    public SpotifyApi(
        RandomStringGenerator randomStringGenerator,
        SpotifyClientInfo clientInfo,
        SpotifyEndpointInfo endpointInfo)
    {
        _httpClient = new HttpClient();
        _randomStringGenerator = randomStringGenerator;
        _clientInfo = clientInfo;
        _endpointInfo = endpointInfo;
    }

    /**
     * responds with an html for a page where the user can click "accept".
     * Might be useful if we want to access information specific to a spotify user.
     */
    public async Task<string> OAuthLogin()
    {
        var state = _randomStringGenerator.GenerateRandomString(16);
        var scope = "user-read-private user-read-email";

        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["response_type"] = "code";
        queryParams["client_id"] = _clientInfo.Id;
        queryParams["scope"] = scope;
        queryParams["redirect_uri"] = _endpointInfo.RedirectUri;
        queryParams["state"] = state;

        var url = $"https://accounts.spotify.com/authorize?{queryParams}";

        Console.WriteLine($"url:{url}");

        return await _httpClient.GetStringAsync(url);
    }

    /**
     * retrieves an oAuth token without user input and therefore has no user-related permissions.
     */
    public async Task<string> NonUserOAuthToken()
    {
        var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientInfo.Id}:{_clientInfo.Secret}"));

        var requestData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://accounts.spotify.com/api/token"),
            Headers = { { "Authorization", $"Basic {authHeader}" } },
            Content = requestData
        };
        
        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        return responseBody
            .ToDto<AccessTokenResponse>()
            ?.access_token ?? throw new NullReferenceException();
    }

    /**
     * [seed_artists] example: 4NHQUGzhtTLFvgF5SZesLK
     */
    public async Task<RecommendedArtistsDto> Recommendations(string token, string seedArtists)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["limit"] = "10";
        queryParams["seed_artists"] = seedArtists;

        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"{_endpointInfo.BaseUri}/v1/recommendations?{queryParams}"),
            Headers = { { "Authorization", $"Bearer {token}" } },
        };
        
        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        return responseBody
            .ToDto<RecommendedArtistsDto>() ?? throw new NullReferenceException();
    }

    /**
     * Useful for retrieving an artistId from an artistName.
     * 
     * [artistName] example: Genghis Tron
     */
    public async Task<SearchArtistDto> SearchArtist(string token, string artistName)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["type"] = "artist";
        queryParams["q"] = artistName;

        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"{_endpointInfo.BaseUri}/v1/search?{queryParams}"),
            Headers = { { "Authorization", $"Bearer {token}" } },
        };

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        return responseBody
            .ToDto<SearchArtistDto>() ?? throw new NullReferenceException();
    }
}