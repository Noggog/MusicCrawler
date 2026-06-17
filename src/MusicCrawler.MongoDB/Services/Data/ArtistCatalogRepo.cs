using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Interfaces;

namespace MusicCrawler.MongoDB.Services.Data;

public class ArtistCatalogRepo : IArtistCatalogRepo
{
    private const string CollectionName = "artists";
    private const string FieldName = "name";
    private const string FieldImageUrl = "imageUrl";
    private const string FieldLastSeenAt = "lastSeenAt";
    private const string FieldPresent = "present";

    private readonly IMongoDbProvider _mongoDbProvider;

    public ArtistCatalogRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    public async Task<CatalogSyncResult> SyncFromLibrary(IReadOnlyList<ArtistMetadata> artists, DateTimeOffset syncedAt)
    {
        var collection = Collection;
        var syncedAtUtc = syncedAt.UtcDateTime;

        var writes = new List<WriteModel<BsonDocument>>(artists.Count);
        foreach (var artist in artists)
        {
            var name = artist.ArtistKey.ArtistName;
            var update = Builders<BsonDocument>.Update
                .Set(FieldName, name)
                .Set(FieldLastSeenAt, syncedAtUtc)
                .Set(FieldPresent, true);

            // Only set the image when we actually have one, so a Plex sync (which
            // currently supplies no image) never clobbers one backfilled elsewhere
            // (e.g. Deezer in a later phase).
            if (artist.ArtistImageUrl != null)
            {
                update = update.Set(FieldImageUrl, artist.ArtistImageUrl);
            }

            writes.Add(new UpdateOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("_id", name),
                update)
            {
                IsUpsert = true,
            });
        }

        if (writes.Count > 0)
        {
            await collection.BulkWriteAsync(writes);
        }

        // Anything not touched by this sync is no longer in the Plex library.
        var absent = await collection.UpdateManyAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Lt(FieldLastSeenAt, syncedAtUtc),
                Builders<BsonDocument>.Filter.Eq(FieldPresent, true)),
            Builders<BsonDocument>.Update.Set(FieldPresent, false));

        var totalPresent = await collection.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Eq(FieldPresent, true));

        return new CatalogSyncResult(
            Upserted: writes.Count,
            MarkedAbsent: (int)absent.ModifiedCount,
            TotalPresent: (int)totalPresent);
    }

    public async Task<CatalogArtist[]> GetAllPresent()
    {
        var collection = Collection;
        var cursor = await collection.FindAsync(
            Builders<BsonDocument>.Filter.Eq(FieldPresent, true),
            new FindOptions<BsonDocument>
            {
                Sort = Builders<BsonDocument>.Sort.Ascending(FieldName),
            });

        return (await cursor.ToListAsync())
            .Select(ToCatalogArtist)
            .ToArray();
    }

    private static CatalogArtist ToCatalogArtist(BsonDocument doc)
    {
        var name = doc.TryGetValue(FieldName, out var n) && !n.IsBsonNull
            ? n.AsString
            : doc["_id"].AsString;

        var imageUrl = doc.TryGetValue(FieldImageUrl, out var img) && !img.IsBsonNull
            ? img.AsString
            : null;

        var lastSeenAt = doc.TryGetValue(FieldLastSeenAt, out var ls) && ls.IsValidDateTime
            ? new DateTimeOffset(ls.ToUniversalTime(), TimeSpan.Zero)
            : default;

        return new CatalogArtist(new ArtistKey(name), imageUrl, lastSeenAt);
    }
}
