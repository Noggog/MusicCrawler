namespace MusicCrawler.Spotify.Services.Singletons;

/**
 * requirements:
 *      https://docs.google.com/document/d/1mKoThanmaFHvZXRKpsoGgsvToyE7HyI1lqf1iSG3hkg/edit#heading=h.4fw8ke4bldgf
 */
public class SpotifyRepo
{
    private readonly SpotifyApi _spotifyApi;

    public SpotifyRepo(SpotifyApi spotifyApi)
    {
        _spotifyApi = spotifyApi;
    }

    /**
     * [seedArtists] example: 4NHQUGzhtTLFvgF5SZesLK
     */
    public async Task<string> Recommendations(string seedArtists)
    {
        return await _spotifyApi.Recommendations(
            token: await _spotifyApi.NonUserOAuthToken(),
            seedArtists: seedArtists
        );
    }
}