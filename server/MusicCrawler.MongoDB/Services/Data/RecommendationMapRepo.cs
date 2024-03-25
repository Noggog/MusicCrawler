using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Lib;

namespace MusicCrawler.MongoDB.Services.Data;

public class RecommendationMapRepo : IRecommendationMapRepo
{
    private readonly IMongoDbProvider _mongoDbProvider;

    public RecommendationMapRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    public string GetEntireCollectionAsString(string collectionName)
    {
        return _mongoDbProvider.database.GetCollection<BsonDocument>(collectionName)
            .Find(Builders<BsonDocument>.Filter.Empty)
            .ToList()
            .Select(x => x.ToString())
            .JoinToStr(", ");
    }

    public void AddToMap(Dictionary<ArtistKey, ArtistKey[]> map)
    {
        if (!CollectionExists(_mongoDbProvider.database, "collection1"))
        {
            _mongoDbProvider.database.CreateCollection("collection1");
        }

        var collection = _mongoDbProvider.database.GetCollection<BsonDocument>("collection1");

        foreach (var kvp in map)
        {
            var keyDocument = new BsonDocument
            {
                { "artistKey", kvp.Key.ToString() }
            };

            var relatedKeysArray = new BsonArray();
            foreach (var relatedKey in kvp.Value)
            {
                relatedKeysArray.Add(relatedKey.ToString());
            }

            keyDocument.Add("relatedKeys", relatedKeysArray);

            Console.WriteLine("Inserting keyDocument:" + keyDocument);
            
            collection.InsertOne(keyDocument);
        }
    }

    public Dictionary<ArtistKey, ArtistKey[]> GetMap()
    {
        throw new NotImplementedException();
    }

    private bool CollectionExists(IMongoDatabase database, string collectionName)
    {
        var filter = new BsonDocument("name", collectionName);
        var collections = database.ListCollections(new ListCollectionsOptions { Filter = filter });
        return collections.Any();
    }
}