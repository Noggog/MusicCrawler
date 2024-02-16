using MusicCrawler.Lib;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeRecommendationRepo : IRecommendationRepo
{
    public async Task<ArtistKey[]> RecommendArtistsFrom(ArtistKey artist)
    {
        return new[]
        {
            new ArtistKey("fakeArtistName1"),
            new ArtistKey("fakeArtistName2"),
        };
    }

    public async Task<ArtistKey[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artists)
    {
        return new[]
        {
            new ArtistKey("fakeArtistName1"),
            new ArtistKey("fakeArtistName2"),
        };
    }
}