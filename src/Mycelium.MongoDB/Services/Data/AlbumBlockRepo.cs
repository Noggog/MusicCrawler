using MongoDB.Bson;
using MongoDB.Driver;
using Mycelium.Interfaces;

namespace Mycelium.MongoDB.Services.Data;

/// <summary>
/// Mongo-backed store of globally blocked albums (see <see cref="IAlbumBlockRepo"/>). One doc per
/// block in the "blockedAlbums" collection, keyed by a lower-cased (artist, album) so blocking the
/// same release twice just refreshes it. The canonical lookup key (which folds typography via the
/// title normalizer) is rebuilt on the Backend side; this _id only dedupes storage — the same split
/// <see cref="AlbumMatchOverrideRepo"/> uses.
/// </summary>
public class AlbumBlockRepo : IAlbumBlockRepo
{
    private const string CollectionName = "blockedAlbums";
    private const string FieldArtist = "artist";
    private const string FieldAlbum = "album";
    private const string FieldBlockedBy = "blockedBy";
    private const string FieldCreatedAt = "createdAt";

    private readonly IMongoDbProvider _mongoDbProvider;

    public AlbumBlockRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    public async Task<AlbumBlock[]> GetAll()
    {
        var cursor = await Collection.FindAsync(Builders<BsonDocument>.Filter.Empty);
        return (await cursor.ToListAsync()).Select(ToBlock).ToArray();
    }

    public Task Add(AlbumBlock block)
    {
        var update = Builders<BsonDocument>.Update
            .SetOnInsert(FieldCreatedAt, DateTimeOffset.UtcNow.UtcDateTime)
            .Set(FieldArtist, block.Artist)
            .Set(FieldAlbum, block.Album)
            .Set(FieldBlockedBy, block.BlockedBy);

        return Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", Id(block.Artist, block.Album)),
            update,
            new UpdateOptions { IsUpsert = true });
    }

    public Task Remove(string artist, string album) =>
        Collection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", Id(artist, album)));

    private static string Id(string artist, string album) =>
        $"{artist.ToLowerInvariant()}|{album.ToLowerInvariant()}";

    private static AlbumBlock ToBlock(BsonDocument doc)
    {
        string? Str(string f) => doc.TryGetValue(f, out var v) && !v.IsBsonNull ? v.AsString : null;
        return new AlbumBlock(Str(FieldArtist) ?? "", Str(FieldAlbum) ?? "", Str(FieldBlockedBy));
    }
}
