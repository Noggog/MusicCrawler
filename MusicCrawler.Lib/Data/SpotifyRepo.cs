using MusicCrawler.Lib.Spotify;

namespace MusicCrawler.Lib.Data;

/**
 * requirements:
 *      https://docs.google.com/document/d/1mKoThanmaFHvZXRKpsoGgsvToyE7HyI1lqf1iSG3hkg/edit#heading=h.4fw8ke4bldgf
 */
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