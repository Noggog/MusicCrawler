using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Interfaces;

namespace MusicCrawler.MongoDB.Services.Data;

/// <summary>
/// Mongo-backed shared acquisition list. One doc per item in the "purchases" collection, keyed by
/// <see cref="PurchaseKey"/>. Global (not per-user) — the unified maintainer queue. Display fields
/// are refreshed on every upsert; status/requestedAt are insert-only so a reconcile never demotes a
/// Sent/InLibrary row.
/// </summary>
public class PurchaseRepo : IPurchaseRepo
{
    private const string CollectionName = "purchases";
    private const string FieldKind = "kind";
    private const string FieldArtist = "artist";
    private const string FieldAlbum = "album";
    private const string FieldImageUrl = "imageUrl";
    private const string FieldScore = "score";
    private const string FieldSources = "sources";
    private const string FieldStatus = "status";
    private const string FieldRequestedAt = "requestedAt";
    private const string FieldSentAt = "sentAt";

    private readonly IMongoDbProvider _mongoDbProvider;

    public PurchaseRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    public async Task<PurchaseItem[]> GetAll()
    {
        var cursor = await Collection.FindAsync(
            Builders<BsonDocument>.Filter.Empty,
            new FindOptions<BsonDocument> { Sort = Builders<BsonDocument>.Sort.Descending(FieldRequestedAt) });
        return (await cursor.ToListAsync()).Select(ToItem).ToArray();
    }

    public Task Upsert(PurchaseItem item)
    {
        var update = Builders<BsonDocument>.Update
            .SetOnInsert(FieldStatus, PurchaseStatus.Pending.ToString())
            .SetOnInsert(FieldRequestedAt, DateTimeOffset.UtcNow.UtcDateTime)
            .Set(FieldKind, item.Kind.ToString())
            .Set(FieldArtist, item.Artist.ArtistName)
            .Set(FieldAlbum, (BsonValue)(item.Album ?? (BsonValue)BsonNull.Value))
            .Set(FieldImageUrl, (BsonValue)(item.ImageUrl ?? (BsonValue)BsonNull.Value))
            .Set(FieldScore, item.Score)
            .Set(FieldSources, new BsonArray(item.Sources));

        return Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", item.Id),
            update,
            new UpdateOptions { IsUpsert = true });
    }

    public async Task<bool> SetStatus(string id, PurchaseStatus status)
    {
        var update = Builders<BsonDocument>.Update.Set(FieldStatus, status.ToString());
        if (status == PurchaseStatus.Sent)
        {
            update = update.Set(FieldSentAt, DateTimeOffset.UtcNow.UtcDateTime);
        }

        var result = await Collection.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), update);
        return result.MatchedCount > 0;
    }

    public Task Remove(string id) =>
        Collection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id));

    private static PurchaseItem ToItem(BsonDocument doc)
    {
        string Str(string f) => doc.TryGetValue(f, out var v) && !v.IsBsonNull ? v.AsString : "";
        string? StrN(string f) => doc.TryGetValue(f, out var v) && !v.IsBsonNull ? v.AsString : null;

        var kind = Enum.TryParse<FeedKind>(Str(FieldKind), out var k) ? k : FeedKind.RecommendedArtist;
        var status = Enum.TryParse<PurchaseStatus>(Str(FieldStatus), out var s) ? s : PurchaseStatus.Pending;
        var sources = doc.TryGetValue(FieldSources, out var src) && src.IsBsonArray
            ? src.AsBsonArray.Select(x => x.AsString).ToArray()
            : Array.Empty<string>();
        var score = doc.TryGetValue(FieldScore, out var sc) && sc.IsNumeric ? sc.ToDouble() : 0;
        var requestedAt = doc.TryGetValue(FieldRequestedAt, out var ra) && ra.IsValidDateTime
            ? (DateTimeOffset)ra.ToUniversalTime()
            : DateTimeOffset.MinValue;
        DateTimeOffset? sentAt = doc.TryGetValue(FieldSentAt, out var sa) && sa.IsValidDateTime
            ? (DateTimeOffset)sa.ToUniversalTime()
            : null;

        return new PurchaseItem(
            doc["_id"].AsString, kind, new ArtistKey(Str(FieldArtist)), StrN(FieldAlbum),
            StrN(FieldImageUrl), score, sources, status, requestedAt, sentAt);
    }
}
