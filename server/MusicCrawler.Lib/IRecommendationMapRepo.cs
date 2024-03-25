namespace MusicCrawler.Lib;

public interface IRecommendationMapRepo
{
    string GetString();
    void AddToMap(Dictionary<ArtistKey, ArtistKey[]> map);
    Dictionary<ArtistKey, ArtistKey[]> GetMap();
}