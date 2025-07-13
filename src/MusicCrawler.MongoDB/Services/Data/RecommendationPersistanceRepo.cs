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
    
    // TODO: I haven't refactored this yet.
    public async Task AddRecommendations(IEnumerable<Recommendation> recommendations)
    {
        if (!CollectionExists(_mongoDbProvider.database, "recommendations"))
        {
            await _mongoDbProvider.database.CreateCollectionAsync("recommendations");
        }

        var collection = _mongoDbProvider.database.GetCollection<BsonDocument>("recommendations");

        foreach (var rec in recommendations)
        {
            var keyDocument = new BsonDocument
            {
                {
                    Keys.ArtistKey, rec.ArtistKey.ToJson()
                }
            };

            var sourceArtists = new BsonArray();
            foreach (var sourceArtist in rec.SourceArtists)
            {
                sourceArtists.Add(sourceArtist.ToJson());
            }

            keyDocument.Add(Keys.SourceArtists, sourceArtists);

            await collection.ReplaceOneAsync(
                filter: new BsonDocument("_id", rec.ArtistKey.ToJson()),
                options: new ReplaceOptions { IsUpsert = true },
                replacement: keyDocument);
        }
    }

    public async Task<IEnumerable<Recommendation>> GetRecommendations()
    {
        var collectionName = "recommendations";
        var collection = _mongoDbProvider.database.GetCollection<BsonDocument>(collectionName);

        var recommendations = new List<Recommendation>();

        var filter = Builders<BsonDocument>.Filter.Empty;
        var documents = (await collection.FindAsync(filter)).ToList();

        foreach (var document in documents)
        {
            var artistKey = JsonSerializer.Deserialize<ArtistKey>(document[Keys.ArtistKey].AsString) ?? throw new Exception($"Could not deserialize: {document[Keys.ArtistKey].AsString}");
            var relatedKeysArray = document[Keys.SourceArtists].AsBsonArray;
            var relatedKeys = new ArtistKey[relatedKeysArray.Count];
            for (int i = 0; i < relatedKeysArray.Count; i++)
            {
                relatedKeys[i] = JsonSerializer.Deserialize<ArtistKey>(relatedKeysArray[i].AsString) ?? throw new Exception($"Could not deserialize: {relatedKeysArray[i].AsString}");
            }

            recommendations.Add(new Recommendation(artistKey, relatedKeys));
        }

        return recommendations;
    }

    private bool CollectionExists(IMongoDatabase database, string collectionName)
    {
        var filter = new BsonDocument("name", collectionName);
        var collections = database.ListCollections(new ListCollectionsOptions { Filter = filter });
        return collections.Any();
    }
}