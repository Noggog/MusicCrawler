using MusicCrawler.Lib;
using Noggog;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeRecommendationRepo : IRecommendationRepo
{
    private readonly Dictionary<ArtistKey, ArtistKey[]> _dictionary;

    public FakeRecommendationRepo(Dictionary<ArtistKey, ArtistKey[]> dictionary)
    {
        _dictionary = dictionary;
    }

    public async Task<Recommendation[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys)
    {
        Dictionary<ArtistKey, List<ArtistKey>> returnDictionary = new();

        foreach (var artist in artistKeys)
        {
            if (_dictionary.TryGetValue(artist, out var recommendations))
            {
                foreach (var recommendation in recommendations)
                {
                    returnDictionary.GetOrAdd(recommendation)
                        .Add(artist);
                }
            }
        }

        return returnDictionary.Select(x => new Recommendation(x.Key, x.Value.ToArray())).ToArray();
    }
}