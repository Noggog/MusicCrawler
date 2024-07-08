using System.Text.Json;
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
        if (!CollectionExists(_mongoDbProvider.database, "recommendations"))
        {
            await _mongoDbProvider.database.CreateCollectionAsync("recommendations");
        }

        var collection = _mongoDbProvider.database.GetCollection<BsonDocument>("recommendations");

        foreach (var kvp in map)
        {
            var keyDocument = new BsonDocument
            {
                {
                    Keys.ArtistKey, kvp.Key.ToJson()
                }
            };

            var relatedKeysArray = new BsonArray();
            foreach (var relatedKey in kvp.Value)
            {
                relatedKeysArray.Add(relatedKey.ToJson());
            }

            keyDocument.Add(Keys.SourceArtists, relatedKeysArray);

            await collection.ReplaceOneAsync(
                filter: new BsonDocument("_id", kvp.Key.ToJson()),
                options: new ReplaceOptions { IsUpsert = true },
                replacement: keyDocument);
        }
    }

    // TODO: I haven't refactored this yet.
    // TODO: Make this async.
    public async Task<Dictionary<ArtistKey, ArtistKey[]>> GetMap()
    {
        var collectionName = "recommendations";
        var collection = _mongoDbProvider.database.GetCollection<BsonDocument>(collectionName);

        var map = new Dictionary<ArtistKey, ArtistKey[]>();

        var filter = Builders<BsonDocument>.Filter.Empty;
        var documents = collection.Find(filter).ToList();

        foreach (var document in documents)
        {
            var artistKey = JsonSerializer.Deserialize<ArtistKey>(document[Keys.ArtistKey].AsString) ?? throw new Exception($"Could not deserialize: {document[Keys.ArtistKey].AsString}");
            var relatedKeysArray = document[Keys.SourceArtists].AsBsonArray;
            var relatedKeys = new ArtistKey[relatedKeysArray.Count];
            for (int i = 0; i < relatedKeysArray.Count; i++)
            {
                relatedKeys[i] = JsonSerializer.Deserialize<ArtistKey>(relatedKeysArray[i].AsString) ?? throw new Exception($"Could not deserialize: {relatedKeysArray[i].AsString}");
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