using MongoDB.Driver;

namespace Mycelium.MongoDB;

public interface IMongoDbProvider
{
    public IMongoDatabase database { get; }
}