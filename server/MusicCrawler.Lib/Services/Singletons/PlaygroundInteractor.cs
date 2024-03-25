namespace MusicCrawler.Lib.Services.Singletons;

public class PlaygroundInteractor
{
    private readonly IRecommendationMapRepo _recommendationMapRepo;

    public PlaygroundInteractor(
        IRecommendationMapRepo recommendationMapRepo)
    {
        _recommendationMapRepo = recommendationMapRepo;
    }

    public async Task<string> GetString()
    {
        await _recommendationMapRepo.AddToMap(new Dictionary<ArtistKey, ArtistKey[]>
        {
            {
                new ArtistKey("artistName1"),
                new[]
                {
                    new ArtistKey("artistName2"),
                    new ArtistKey("artistName3"),
                }
            }
        });

        await _recommendationMapRepo.AddToMap(new Dictionary<ArtistKey, ArtistKey[]>
        {
            {
                new ArtistKey("artistName3"),
                new[]
                {
                    new ArtistKey("artistName4"),
                    new ArtistKey("artistName5"),
                }
            }
        });

        return (await _recommendationMapRepo.GetMap())
            .ToLogStr();
    }
}