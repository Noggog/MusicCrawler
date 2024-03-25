namespace MusicCrawler.Lib;

public interface IRecommendationMapRepo
{
    string GetEntireCollectionAsString(string collectionName);
    void AddToMap(Dictionary<ArtistKey, ArtistKey[]> map);
    Dictionary<ArtistKey, ArtistKey[]> GetMap();
}