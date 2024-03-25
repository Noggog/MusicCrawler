using MongoDB.Driver;

namespace MusicCrawler.MongoDB.Services.Singletons;

public class MongoDbWrapper
{
    private readonly MongoClient client;
    public readonly IMongoDatabase database;

    public MongoDbWrapper()
    {
        client = new MongoClient(Environment.GetEnvironmentVariable("mongoURI") ?? throw new InvalidOperationException());
        database = client.GetDatabase("sample_mflix");
    }
}