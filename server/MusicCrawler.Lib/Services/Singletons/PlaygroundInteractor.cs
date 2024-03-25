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
        return _recommendationMapRepo.GetString();
    }
}