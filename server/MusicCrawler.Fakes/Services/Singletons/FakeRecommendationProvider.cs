using MusicCrawler.Lib;
using Noggog;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeRecommendationProvider : IRecommendationProvider
{
    private readonly Dictionary<ArtistKey, ArtistKey[]> _sourceArtistToRecommendedArtistDict;

    public FakeRecommendationProvider(Dictionary<ArtistKey, ArtistKey[]> sourceArtistToRecommendedArtistDict)
    {
        _sourceArtistToRecommendedArtistDict = sourceArtistToRecommendedArtistDict;
    }

    public async Task<Recommendation[]> RecommendArtistsFrom(ArtistKey artistKey)
    {
        if (!_sourceArtistToRecommendedArtistDict.TryGetValue(artistKey, out var recommendations))
        {
            return Array.Empty<Recommendation>();
        }

        var recommender = new ArtistKey[] { artistKey }; 
        return recommendations
            .Select(it => new Recommendation(it, recommender))
            .ToArray();
    }

    public async Task<Recommendation[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys)
    {
        var found = artistKeys
            .Distinct()
            .Select(x => (x, _sourceArtistToRecommendedArtistDict.GetOrDefault(x)))
            .ToArray();
        var ret = new Dictionary<ArtistKey, List<ArtistKey>>();
        foreach (var f in found)
        {
            if (f.Item2 == null) continue;
            foreach (var recommendation in f.Item2)
            {
                ret.GetOrAdd(recommendation)
                    .Add(f.x);
            }
        }
        return ret
            .Select(x => new Recommendation(x.Key, x.Value.ToArray()))
            .ToArray();
    }
}