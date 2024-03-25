﻿using MongoDB.Bson;
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

    // TODO: I haven't refactored this yet.
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
            
            collection.InsertOne(keyDocument);
        }
    }

    // TODO: I haven't refactored this yet.
    public Dictionary<ArtistKey, ArtistKey[]> GetMap()
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