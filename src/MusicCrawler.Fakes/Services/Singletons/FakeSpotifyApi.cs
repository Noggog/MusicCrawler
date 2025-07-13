using MusicCrawler.Lib;
using MusicCrawler.Spotify.Models;
using MusicCrawler.Spotify.Services.Singletons;
using Newtonsoft.Json;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeSpotifyApi : ISpotifyApi
{
    public Task<string> OAuthLogin()
    {
        throw new NotImplementedException();
    }

    public async Task<string> NonUserOAuthToken()
    {
        return "fakeOAuthToken";
    }

    public async Task<RecommendedArtistsDto> Recommendations(string token, string seedArtists)
    {
        return JsonConvert.DeserializeObject<RecommendedArtistsDto>(FakeJsons.recommendation_2023_02_24)!;
    }

    public async Task<SearchArtistDto> SearchArtist(string token, string artistName)
    {
        return JsonConvert.DeserializeObject<SearchArtistDto>(FakeJsons.searchArtist_2023_02_24)!;
    }
}