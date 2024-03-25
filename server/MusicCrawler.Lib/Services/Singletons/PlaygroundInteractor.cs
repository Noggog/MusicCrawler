namespace MusicCrawler.Lib.Services.Singletons;

public class PlaygroundInteractor
{
    private readonly IRecommendationMapRepo _recommendationMapRepo;

    public PlaygroundInteractor(
        IRecommendationMapRepo recommendationMapRepo)
    {
        _recommendationMapRepo = recommendationMapRepo;
    }

    public string GetString()
    {
        _recommendationMapRepo.AddToMap(new Dictionary<ArtistKey, ArtistKey[]>
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

        return _recommendationMapRepo.GetEntireCollectionAsString("collection1");
    }
}