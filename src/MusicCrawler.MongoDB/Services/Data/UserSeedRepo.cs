using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Interfaces;

namespace MusicCrawler.MongoDB.Services.Data;

/// <summary>
/// Mongo-backed per-user seed store. One document per user in the "userSeeds" collection
/// (_id = userId) holding a set of artist names; add/remove are atomic $addToSet / $pull.
/// </summary>
public class UserSeedRepo : IUserSeedRepo
{
    private const string CollectionName = "userSeeds";
    private const string FieldSeeds = "seeds";

    private readonly IMongoDbProvider _mongoDbProvider;

    public UserSeedRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    public async Task<string[]> GetSeeds(string userId)
    {
        var cursor = await Collection.FindAsync(Builders<BsonDocument>.Filter.Eq("_id", userId));
        var doc = await cursor.FirstOrDefaultAsync();
        if (doc == null || !doc.TryGetValue(FieldSeeds, out var seeds) || !seeds.IsBsonArray)
        {
            return Array.Empty<string>();
        }

        return seeds.AsBsonArray
            .Where(s => !s.IsBsonNull)
            .Select(s => s.AsString)
            .ToArray();
    }

    public Task AddSeed(string userId, string artistName) =>
        Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.AddToSet(FieldSeeds, artistName),
            new UpdateOptions { IsUpsert = true });

    public Task RemoveSeed(string userId, string artistName) =>
        Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.Pull(FieldSeeds, artistName));
}
