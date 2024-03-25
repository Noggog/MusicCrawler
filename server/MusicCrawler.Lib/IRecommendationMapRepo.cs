namespace MusicCrawler.Lib;

public interface IRecommendationMapRepo
{
    string GetEntireCollectionAsString(string collectionName);
    Task AddToMap(Dictionary<ArtistKey, ArtistKey[]> map);
    Task<Dictionary<ArtistKey, ArtistKey[]>> GetMap();
}