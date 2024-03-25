namespace MusicCrawler.Lib.Services.Singletons;

public class PlaygroundInteractor
{
    private readonly IRecommendationMapRepo _recommendationMapRepo;
    private readonly RecommendationInteractor _recommendationInteractor;

    public PlaygroundInteractor(
        IRecommendationMapRepo recommendationMapRepo,
        RecommendationInteractor recommendationInteractor)
    {
        _recommendationMapRepo = recommendationMapRepo;
        _recommendationInteractor = recommendationInteractor;
    }

    public async Task<string> GetString()
    {
        return (await _recommendationInteractor.Recommendations())
            .Count()
            .Also(x => Console.WriteLine("first count:" + x))
            .plus(
                (await _recommendationInteractor.Recommendations())
                .Count()
                .Also(x => Console.WriteLine("second count:" + x))
            )
            .ToString();
    }
}