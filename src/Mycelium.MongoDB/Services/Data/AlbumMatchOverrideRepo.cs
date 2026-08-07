using MongoDB.Bson;
using MongoDB.Driver;
using Mycelium.Interfaces;

namespace Mycelium.MongoDB.Services.Data;

/// <summary>
/// Mongo-backed store of manual album merges (see <see cref="IAlbumMatchOverrideRepo"/>). One doc per
/// merge in the "albumMatchOverrides" collection, keyed by a lower-cased (match-artist, Deezer title)
/// so recording the same merge twice just refreshes it. The canonical lookup key (which folds
/// typography via the title normalizer) is rebuilt on the Backend side; this _id only dedupes storage.
/// </summary>
public class AlbumMatchOverrideRepo : IAlbumMatchOverrideRepo
{
    private const string CollectionName = "albumMatchOverrides";
    private const string FieldMatchArtist = "matchArtist";
    private const string FieldDeezerTitle = "deezerTitle";
    private const string FieldLibraryTitle = "libraryTitle";
    private const string FieldCreatedAt = "createdAt";

    private readonly IMongoDbProvider _mongoDbProvider;

    public AlbumMatchOverrideRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    public async Task<AlbumMatchOverride[]> GetAll()
    {
        var cursor = await Collection.FindAsync(Builders<BsonDocument>.Filter.Empty);
        return (await cursor.ToListAsync()).Select(ToOverride).ToArray();
    }

    public Task Add(AlbumMatchOverride @override)
    {
        var update = Builders<BsonDocument>.Update
            .SetOnInsert(FieldCreatedAt, DateTimeOffset.UtcNow.UtcDateTime)
            .Set(FieldMatchArtist, @override.MatchArtist)
            .Set(FieldDeezerTitle, @override.DeezerTitle)
            .Set(FieldLibraryTitle, @override.LibraryTitle);

        return Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", Id(@override.MatchArtist, @override.DeezerTitle)),
            update,
            new UpdateOptions { IsUpsert = true });
    }

    private static string Id(string matchArtist, string deezerTitle) =>
        $"{matchArtist.ToLowerInvariant()}|{deezerTitle.ToLowerInvariant()}";

    private static AlbumMatchOverride ToOverride(BsonDocument doc)
    {
        string Str(string f) => doc.TryGetValue(f, out var v) && !v.IsBsonNull ? v.AsString : "";
        return new AlbumMatchOverride(Str(FieldMatchArtist), Str(FieldDeezerTitle), Str(FieldLibraryTitle));
    }
}
