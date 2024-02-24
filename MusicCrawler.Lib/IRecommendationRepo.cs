namespace MusicCrawler.Lib;

/// <summary>
/// Interface for retrieving recommendations, given specific information
/// </summary>
public interface IRecommendationRepo
{
    Task<Recommendation[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys);
}