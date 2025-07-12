using Mongo2Go;
using MongoDB.Driver;
using MusicCrawler.MongoDB;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeMongoDbProvider : IDisposable, IMongoDbProvider
{
    private readonly IMongoClient _client;
    private readonly MongoDbRunner _runner;
    public IMongoDatabase database { get; }

    public FakeMongoDbProvider()
    {
        _runner = MongoDbRunner.Start();
        _client = new MongoClient(_runner.ConnectionString);
        database = _client.GetDatabase("test_database");
    }

    public void Dispose()
    {
        _client.DropDatabase("test_database");
        _client.DropDatabase("admin");
        _client.DropDatabase("local");
        _runner.Dispose();
    }
}