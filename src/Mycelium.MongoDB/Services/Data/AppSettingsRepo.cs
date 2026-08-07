using MongoDB.Bson;
using MongoDB.Driver;
using Mycelium.Interfaces;

namespace Mycelium.MongoDB.Services.Data;

/// <summary>
/// Mongo-backed operator settings (see <see cref="IAppSettingsRepo"/>). One doc per settings group in
/// the "appSettings" collection — the download switch lives in <c>_id: "downloads"</c>. A missing doc
/// or field means "never set", which the caller reads as "use the environment default".
/// </summary>
public class AppSettingsRepo : IAppSettingsRepo
{
    private const string CollectionName = "appSettings";
    private const string DownloadsId = "downloads";
    private const string FieldAutomatic = "automatic";
    private const string FieldUpdatedAt = "updatedAt";

    private readonly IMongoDbProvider _mongoDbProvider;

    public AppSettingsRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    public async Task<bool?> GetDownloadsAutomatic()
    {
        var cursor = await Collection.FindAsync(Builders<BsonDocument>.Filter.Eq("_id", DownloadsId));
        var doc = await cursor.FirstOrDefaultAsync();
        return doc != null && doc.TryGetValue(FieldAutomatic, out var v) && v.IsBoolean
            ? v.AsBoolean
            : null;
    }

    public Task SetDownloadsAutomatic(bool automatic) =>
        Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", DownloadsId),
            Builders<BsonDocument>.Update
                .Set(FieldAutomatic, automatic)
                .Set(FieldUpdatedAt, DateTimeOffset.UtcNow.UtcDateTime),
            new UpdateOptions { IsUpsert = true });
}
