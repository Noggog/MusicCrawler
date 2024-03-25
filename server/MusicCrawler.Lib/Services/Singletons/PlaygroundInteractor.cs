using MongoDB.Bson;
using MongoDB.Driver;

namespace MusicCrawler.Lib.Services.Singletons;

public class PlaygroundInteractor
{
    public string getString()
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