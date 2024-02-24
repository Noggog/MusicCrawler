using MusicCrawler.Lib;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeRecommendationRepo : IRecommendationRepo
{
    public async Task<IEnumerable<ArtistKey>> RecommendArtistsFrom(ArtistKey artist)
    {
        return new[]
        {
            new ArtistKey("fakeArtistName1"),
            new ArtistKey("fakeArtistName2"),
        };
    }

    public async Task<IEnumerable<ArtistKey>> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys)
    {
        return new[]
        {
            new ArtistKey("fakeArtistName1"),
            new ArtistKey("fakeArtistName2"),
        };
    }
}