using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Lib;

namespace MusicCrawler.MongoDB.Services.Singletons;

public class RecommendationMapRepo : IRecommendationMapRepo
{
    private readonly MongoDbWrapper _mongoDbWrapper;

    public RecommendationMapRepo(MongoDbWrapper mongoDbWrapper)
    {
        _mongoDbWrapper = mongoDbWrapper;
    }

    public string GetString()
    {
        return _mongoDbWrapper.database.GetCollection<BsonDocument>("comments")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .ToList()
            .Select(x => x.ToString())
            .JoinToStr(", ");
    }
}