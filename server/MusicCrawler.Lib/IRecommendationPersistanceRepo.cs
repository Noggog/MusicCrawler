namespace MusicCrawler.Lib;

public interface IRecommendationPersistanceRepo
{
    string GetEntireCollectionAsString(string collectionName);
    Task AddToMap(Dictionary<ArtistKey, ArtistKey[]> map);
    Task<Dictionary<ArtistKey, ArtistKey[]>> GetMap();
}