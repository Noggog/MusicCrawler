using System.Diagnostics;
using System.Text;
using System.Web;
using Newtonsoft.Json;

namespace MusicCrawler.Lib.Spotify;

/**
 * docs:
 *      https://developer.spotify.com/documentation/web-api/reference/get-recommendations
 *      https://developer.spotify.com/documentation/web-api/concepts/authorization
 *      https://developer.spotify.com/documentation/web-api/tutorials/code-flow
 *      https://developer.spotify.com/documentation/web-api/tutorials/client-credentials-flow
 */
public class SpotifyApi
{
    private readonly HttpClient httpClient;

    private readonly string baseUri;

    // The client ID from https://developer.spotify.com/dashboard
    private readonly string client_id = "267c94026025449b8013ddde6d959e13";

    // The client secret from https://developer.spotify.com/dashboard
    private readonly string client_secret = "92c88db9315545e38989ca8cc4cad2ad";

    // This is the page that the user will be sent to after clicking "accept" from the html given by accounts.spotify.com/authorize.
    private readonly string redirect_uri = "http://localhost/";

    public SpotifyApi()
    {
        this.baseUri = "https://api.spotify.com";
        this.httpClient = new HttpClient();
    }

    public SpotifyApi(string baseUri)
    {
        this.baseUri = baseUri;
        this.httpClient = new HttpClient();
    }


    /**
     * responds with an html for a page where the user can click "accept".
     * Might be useful if we want to access information specific to a spotify user.
     */
    public async Task<string> OAuthLogin()
    {
        var state = RandomStringGenerator.GenerateRandomString(16);
        var scope = "user-read-private user-read-email";

        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["response_type"] = "code";
        queryParams["client_id"] = client_id;
        queryParams["scope"] = scope;
        queryParams["redirect_uri"] = redirect_uri;
        queryParams["state"] = state;

        var url = $"https://accounts.spotify.com/authorize?{queryParams}";

        Console.WriteLine($"url:{url}");

        var response = await httpClient.GetStringAsync(url);

        return response;
    }

    /**
     * retrieves an oAuth token without user input and therefore has no user-related permissions.
     */
    public async Task<string> NonUserOAuthToken()
    {
        var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client_id}:{client_secret}"));

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

        var response = await httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        var token = JsonConvert.DeserializeObject<AccessTokenResponse>(responseBody)?.access_token;

        Debug.Assert(token != null, nameof(token) + " != null");
        return token;
    }

    /**
     * [seed_artists] example: 4NHQUGzhtTLFvgF5SZesLK
     */
    public async Task<string> Recommendations(string token, string seedArtists)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["limit"] = "10";
        queryParams["seed_artists"] = seedArtists;
        
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"https://api.spotify.com/v1/recommendations?{queryParams}"),
            Headers = { { "Authorization", $"Bearer {token}" } },
        };

        var response = await httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        return responseBody;
    }
}