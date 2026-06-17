using MongoDB.Driver;

namespace MusicCrawler.MongoDB.Services.Environment;

public class MongoDbProvider : IMongoDbProvider
{
    public IMongoDatabase database { get; }

    public MongoDbProvider()
    {
        var client = new MongoClient(System.Environment.GetEnvironmentVariable("MONGO_URI") ?? throw new InvalidOperationException());
        database = client.GetDatabase("MusicCrawler");
    }
}