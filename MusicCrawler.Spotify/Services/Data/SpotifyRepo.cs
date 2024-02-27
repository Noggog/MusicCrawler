using MusicCrawler.Lib;
using MusicCrawler.Spotify.Services.Singletons;
using Noggog;

namespace MusicCrawler.Spotify.Services.Data;

/**
 * requirements:
 *      https://docs.google.com/document/d/1mKoThanmaFHvZXRKpsoGgsvToyE7HyI1lqf1iSG3hkg/edit#heading=h.4fw8ke4bldgf
 */
public class SpotifyRepo : IRecommendationRepo
{
    private readonly ISpotifyApi _spotifyApi;

    public SpotifyRepo(ISpotifyApi spotifyApi)
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

    public async Task<Recommendation[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys)
    {
        Dictionary<ArtistKey, List<ArtistKey>> returningDictionary = new();
        
        foreach (var artistKey in artistKeys)
        {
            var artistId = await GetArtistId(artistKey.ArtistName);
            var recommendedArtistsDto = await _spotifyApi.Recommendations(token: await _spotifyApi.NonUserOAuthToken(), artistId);
            var recommendedArtists = recommendedArtistsDto.Tracks.SelectMany(track => track.Artists);

            foreach (var recommendedArtist in recommendedArtists)
            {
                returningDictionary.GetOrAdd(new ArtistKey(recommendedArtist.Name))
                    .Add(artistKey);
            }
        }
    
        return returningDictionary.Select(x => new Recommendation(x.Key, x.Value.ToArray())).ToArray();
    }
}