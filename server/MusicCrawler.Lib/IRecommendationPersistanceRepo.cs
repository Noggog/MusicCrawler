namespace MusicCrawler.Lib;

public interface IRecommendationPersistanceRepo
{
    Task AddToMap(Dictionary<ArtistKey, ArtistKey[]> map);
    Task<Dictionary<ArtistKey, ArtistKey[]>> GetMap();
}