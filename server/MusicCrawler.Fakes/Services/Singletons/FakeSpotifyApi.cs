using MusicCrawler.Lib;
using MusicCrawler.Spotify.Models;
using MusicCrawler.Spotify.Services.Singletons;

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
        return FakeJsons.recommendation_2023_02_24
            .ToDto<RecommendedArtistsDto>() ?? throw new NullReferenceException();
    }

    public async Task<SearchArtistDto> SearchArtist(string token, string artistName)
    {
        return FakeJsons.searchArtist_2023_02_24
            .ToDto<SearchArtistDto>() ?? throw new NullReferenceException();
    }
}