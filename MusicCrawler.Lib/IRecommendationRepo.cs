namespace MusicCrawler.Lib;

/// <summary>
/// Interface for retrieving recommendations, given specific information
/// </summary>
public interface IRecommendationRepo
{
    Task<IEnumerable<ArtistKey>> RecommendArtistsFrom(ArtistKey artist);
    Task<IEnumerable<ArtistKey>> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys);
}