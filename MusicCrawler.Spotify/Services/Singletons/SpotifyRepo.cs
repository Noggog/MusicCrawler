using MusicCrawler.Lib;

namespace MusicCrawler.Spotify.Services.Singletons;

/**
 * requirements:
 *      https://docs.google.com/document/d/1mKoThanmaFHvZXRKpsoGgsvToyE7HyI1lqf1iSG3hkg/edit#heading=h.4fw8ke4bldgf
 */
public class SpotifyRepo : IRecommendationRepo
{
    private readonly SpotifyApi _spotifyApi;

    public SpotifyRepo(SpotifyApi spotifyApi)
    {
        _spotifyApi = spotifyApi;
    }

    public async Task<string> GetArtistId(string artistName)
    {
        return (await _spotifyApi.SearchArtist(
                token: await _spotifyApi.NonUserOAuthToken(),
                artistName: artistName
            ))
            .Artists
            .Items
            // The api responds with lots of artists, the first being what it considers the strongest match.
            // However, if we wanted to, we could inspect the others and determine the strongest match in our own custom way.
            .First()
            .Id;
    }

    /**
     * [seedArtists] example: 4NHQUGzhtTLFvgF5SZesLK
     */
    // TODO: do transformations to satisfy the IRecommendationRepo contract.
    public async Task<string> Recommendations(string seedArtists)
    {
        return await _spotifyApi.Recommendations(
            token: await _spotifyApi.NonUserOAuthToken(),
            seedArtists: seedArtists
        );
    }

    public Task<ArtistKey[]> RecommendArtistsFrom(ArtistKey artist)
    {
        throw new NotImplementedException();
    }

    public Task<ArtistKey[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artists)
    {
        throw new NotImplementedException();
    }
}