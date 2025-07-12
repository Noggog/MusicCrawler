namespace MusicCrawler.Lib;

/// <summary>
/// Interface for retrieving recommendations, given specific information
/// </summary>
public interface IRecommendationProvider
{
    Task<Recommendation[]> RecommendArtistsFrom(ArtistKey artistKey);
    Task<Recommendation[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys);
}