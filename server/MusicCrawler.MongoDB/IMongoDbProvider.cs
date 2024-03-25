using MongoDB.Driver;

namespace MusicCrawler.MongoDB;

public interface IMongoDbProvider
{
    public IMongoDatabase database { get; }
}