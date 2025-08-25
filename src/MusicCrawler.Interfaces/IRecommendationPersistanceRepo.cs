namespace MusicCrawler.Interfaces;

public interface IRecommendationPersistenceRepo
{
    Task AddRecommendations(IEnumerable<Recommendation> recommendations);
    Task<IEnumerable<Recommendation>> GetRecommendations();
}