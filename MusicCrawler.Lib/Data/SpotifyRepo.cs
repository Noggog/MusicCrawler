using MusicCrawler.Lib.Spotify;

namespace MusicCrawler.Lib.Data;

public class SpotifyRepo
{
    private SpotifyApi spotifyApi;

    public SpotifyRepo()
    {
        this.spotifyApi = new SpotifyApi();
    }

    public async Task<string> Recommendations()
    {
        return await spotifyApi.Recommendations(await spotifyApi.NonUserOAuthToken());
    }
}