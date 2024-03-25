using MongoDB.Driver;

namespace MusicCrawler.MongoDB.Services.Singletons;

public class MongoDbProvider
{
    private readonly MongoClient client;
    public readonly IMongoDatabase database;

    public MongoDbProvider()
    {
        client = new MongoClient(Environment.GetEnvironmentVariable("mongoURI") ?? throw new InvalidOperationException());
        database = client.GetDatabase("sample_mflix");
    }
}