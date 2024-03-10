using MusicCrawler.Lib;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeRecommendationRepo : IRecommendationRepo
{
    private readonly Dictionary<ArtistKey, ArtistKey[]> _sourceArtistToRecommendedArtistDict;

    public FakeRecommendationRepo(Dictionary<ArtistKey, ArtistKey[]> sourceArtistToRecommendedArtistDict)
    {
        _sourceArtistToRecommendedArtistDict = sourceArtistToRecommendedArtistDict;
    }

    public async Task<Recommendation[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys)
    {
        return _sourceArtistToRecommendedArtistDict
            .Inverse()
            .Select(it => new Recommendation(it.Key, it.Value.ToArray()))
            .ToArray();
    }
}