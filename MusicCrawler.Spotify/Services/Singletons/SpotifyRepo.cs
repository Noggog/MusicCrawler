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

    public Task<IEnumerable<ArtistKey>> RecommendArtistsFrom(ArtistKey artist)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<ArtistKey>> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys)
    {
        return (await Task.WhenAll(artistKeys.Select(async artistKey =>
            {
                var artistId = await GetArtistId(artistKey.ArtistName);
                return await _spotifyApi.Recommendations(token: await _spotifyApi.NonUserOAuthToken(), artistId);
            })))
            .SelectMany(it => it.Tracks.SelectMany(track => track.Artists))
            .Select(artist => new ArtistKey(artist.Name));
    }
}