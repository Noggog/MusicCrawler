namespace MusicCrawler.Lib.Services.Singletons;

public class PlaygroundInteractor
{
    private readonly IRecommendationPersistanceRepo _recommendationPersistanceRepo;
    private readonly RecommendationInteractor _recommendationInteractor;

    public PlaygroundInteractor(
        IRecommendationPersistanceRepo recommendationPersistanceRepo,
        RecommendationInteractor recommendationInteractor)
    {
        _recommendationPersistanceRepo = recommendationPersistanceRepo;
        _recommendationInteractor = recommendationInteractor;
    }

    public async Task<string> GetString()
    {
        (await _recommendationInteractor.Recommendations())
            .Count()
            .Also(x => Console.WriteLine("first count:" + x));
        return (await _recommendationInteractor.Recommendations())
            .Also(x => Console.WriteLine("second count:" + x.Count()))
            .ToLogStr();
    }
}