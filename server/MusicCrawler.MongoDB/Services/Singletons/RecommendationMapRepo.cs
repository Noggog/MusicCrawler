using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Lib;

namespace MusicCrawler.MongoDB.Services.Singletons;

public class RecommendationMapRepo : IRecommendationMapRepo
{
    private readonly IMongoDbProvider _mongoDbProvider;

    public RecommendationMapRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    public string GetString()
    {
        return _mongoDbProvider.database.GetCollection<BsonDocument>("comments")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .ToList()
            .Select(x => x.ToString())
            .JoinToStr(", ");
    }

    public void AddToMap(Dictionary<ArtistKey, ArtistKey[]> map)
    {
        throw new NotImplementedException();
    }

    public Dictionary<ArtistKey, ArtistKey[]> GetMap()
    {
        throw new NotImplementedException();
    }
}