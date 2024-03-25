using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Lib;

namespace MusicCrawler.MongoDB.Services.Singletons;

public class MongoDbWrapper : IRecommendationMapRepo
{
    public string GetString()
    {
        var environmentString =
            Environment.GetEnvironmentVariable("mongoURI") ?? throw new InvalidOperationException();
        Console.WriteLine(environmentString);

        var client = new MongoClient(environmentString);
        var database = client.GetDatabase("sample_mflix");
        var collection = database.GetCollection<BsonDocument>("comments");

        return collection.Find(Builders<BsonDocument>.Filter.Empty).ToList()
            .Select(x => x.ToString())
            .JoinToStr(", ");
    }
}