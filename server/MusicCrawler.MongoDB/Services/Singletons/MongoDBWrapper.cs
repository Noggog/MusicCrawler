using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Lib;

namespace MusicCrawler.MongoDB.Services.Singletons;

public class MongoDbWrapper : IRecommendationMapRepo
{
    public string GetString()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("mongoURI") ?? throw new InvalidOperationException());
        var database = client.GetDatabase("sample_mflix");
        var collection = database.GetCollection<BsonDocument>("comments");

        return collection.Find(Builders<BsonDocument>.Filter.Empty).ToList()
            .Select(x => x.ToString())
            .JoinToStr(", ");
    }
}