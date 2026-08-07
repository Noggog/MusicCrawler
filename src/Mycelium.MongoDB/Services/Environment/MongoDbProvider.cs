using MongoDB.Driver;

namespace Mycelium.MongoDB.Services.Environment;

public class MongoDbProvider : IMongoDbProvider
{
    public IMongoDatabase database { get; }

    public MongoDbProvider()
    {
        var client = new MongoClient(System.Environment.GetEnvironmentVariable("MONGO_URI") ?? throw new InvalidOperationException());
        database = client.GetDatabase("Mycelium");
    }
}