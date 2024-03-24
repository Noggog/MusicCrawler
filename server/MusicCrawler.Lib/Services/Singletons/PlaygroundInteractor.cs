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
        

        return environmentString;

        // var client = new MongoClient("mongodb://localhost:27017");
        // var database = client.GetDatabase("your_database_name");
        // var collection = database.GetCollection<BsonDocument>("your_collection_name");
        //
        //
        // return collection.
    }
}