namespace MusicCrawler.Lib;

public interface IRecommendationPersistanceRepo
{
    Task AddRecommendations(IEnumerable<Recommendation> recommendations);
    Task<IEnumerable<Recommendation>> GetRecommendations();
}