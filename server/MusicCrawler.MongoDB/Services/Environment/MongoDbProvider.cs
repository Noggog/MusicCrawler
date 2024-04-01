using MongoDB.Driver;
using MusicCrawler.Lib.Services.Singletons;

namespace MusicCrawler.MongoDB.Services.Environment;

public class MongoDbProvider : IMongoDbProvider
{
    public IMongoDatabase database { get; }

    public MongoDbProvider(EnvironmentVariableProvider environmentVariableProvider)
    {
        var client = new MongoClient(environmentVariableProvider.MongoURI() ?? throw new InvalidOperationException());
        database = client.GetDatabase("sample_mflix");
    }
}