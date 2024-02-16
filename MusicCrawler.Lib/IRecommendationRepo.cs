namespace MusicCrawler.Lib;

/// <summary>
/// Interface for retrieving recommendations, given specific information
/// </summary>
public interface IRecommendationRepo
{
    Task<ArtistKey[]> RecommendArtistsFrom(ArtistKey artist);
    Task<ArtistKey[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artists);
}