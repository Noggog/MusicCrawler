using MongoDB.Driver;

namespace MusicCrawler.MongoDB.Services.Singletons;

public class MongoDbProvider : IMongoDbProvider
{
    public IMongoDatabase database { get; }

    public MongoDbProvider()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("mongoURI") ?? throw new InvalidOperationException());
        database = client.GetDatabase("sample_mflix");
    }
}