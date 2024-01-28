using MusicCrawler.Lib.Spotify;

namespace MusicCrawler.Lib.Data;

public class SpotifyRepo
{
    private SpotifyApi spotifyApi;

    public SpotifyRepo()
    {
        this.spotifyApi = new SpotifyApi();
    }

    /**
     * [seedArtists] example: 4NHQUGzhtTLFvgF5SZesLK
     */
    public async Task<string> Recommendations(string seedArtists)
    {
        return await spotifyApi.Recommendations(
            token: await spotifyApi.NonUserOAuthToken(),
            seedArtists: seedArtists
        );
    }
}