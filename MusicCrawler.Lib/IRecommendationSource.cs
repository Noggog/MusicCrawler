namespace MusicCrawler.Lib;

/// <summary>
/// Interface for retrieving recommendations, given specific information
/// </summary>
public interface IRecommendationSource
{
    Task<ArtistKey[]> RecommendArtistsFrom(ArtistKey artist);
}