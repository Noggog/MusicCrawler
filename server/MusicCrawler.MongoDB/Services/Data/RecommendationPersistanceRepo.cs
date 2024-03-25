using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Lib;

namespace MusicCrawler.MongoDB.Services.Data;

public class RecommendationPersistanceRepo : IRecommendationPersistanceRepo
{
    private readonly IMongoDbProvider _mongoDbProvider;

    public RecommendationPersistanceRepo(IMongoDbProvider mongoDbProvider)
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

    // TODO: I haven't refactored this yet.
    public async Task AddToMap(Dictionary<ArtistKey, ArtistKey[]> map)
    {
        if (!CollectionExists(_mongoDbProvider.database, "collection1"))
        {
            await _mongoDbProvider.database.CreateCollectionAsync("collection1");
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

            await collection.ReplaceOneAsync(
                filter: new BsonDocument("_id", kvp.Key.ToString()),
                options: new ReplaceOptions { IsUpsert = true },
                replacement: keyDocument);
        }
    }

    // TODO: I haven't refactored this yet.
    // TODO: Make this async.
    public async Task<Dictionary<ArtistKey, ArtistKey[]>> GetMap()
    {
        var collectionName = "collection1";
        var collection = _mongoDbProvider.database.GetCollection<BsonDocument>(collectionName);

        var map = new Dictionary<ArtistKey, ArtistKey[]>();

        var filter = Builders<BsonDocument>.Filter.Empty;
        var documents = collection.Find(filter).ToList();

        foreach (var document in documents)
        {
            var artistKey = new ArtistKey(document["artistKey"].AsString);
            var relatedKeysArray = document["relatedKeys"].AsBsonArray;
            var relatedKeys = new ArtistKey[relatedKeysArray.Count];
            for (int i = 0; i < relatedKeysArray.Count; i++)
            {
                relatedKeys[i] = new ArtistKey(relatedKeysArray[i].AsString);
            }

            map.Add(artistKey, relatedKeys);
        }

        return map;
    }

    private bool CollectionExists(IMongoDatabase database, string collectionName)
    {
        var filter = new BsonDocument("name", collectionName);
        var collections = database.ListCollections(new ListCollectionsOptions { Filter = filter });
        return collections.Any();
    }
}