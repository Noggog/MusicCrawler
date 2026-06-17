using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Interfaces;

namespace MusicCrawler.MongoDB.Services.Data;

/// <summary>
/// Mongo-backed per-user album verdicts. One doc per (user, artist, album) in the
/// "userAlbumRatings" collection, keyed "{userId}:{artist} {album}". The album analogue of the
/// artist ratings in <see cref="UserQueueRepo"/>.
/// </summary>
public class UserAlbumRatingRepo : IUserAlbumRatingRepo
{
    private const string CollectionName = "userAlbumRatings";
    private const string FieldUserId = "userId";
    private const string FieldArtist = "artist";
    private const string FieldAlbum = "album";
    private const string FieldAlbumArt = "albumArt";
    private const string FieldStatus = "status";
    private const string FieldDecidedAt = "decidedAt";

    private static readonly string StatusLiked = DiscoveryStatus.Liked.ToString();

    private readonly IMongoDbProvider _mongoDbProvider;

    public UserAlbumRatingRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    private static string DocId(string userId, string artist, string album) => $"{userId}:{artist} {album}";

    public async Task Rate(string userId, string artistName, string albumName, string? albumArt, DiscoveryStatus status)
    {
        var updates = new List<UpdateDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Update.SetOnInsert(FieldUserId, userId),
            Builders<BsonDocument>.Update.SetOnInsert(FieldArtist, artistName),
            Builders<BsonDocument>.Update.SetOnInsert(FieldAlbum, albumName),
            Builders<BsonDocument>.Update.Set(FieldStatus, status.ToString()),
            Builders<BsonDocument>.Update.Set(FieldDecidedAt, DateTimeOffset.UtcNow.UtcDateTime),
        };
        if (albumArt != null)
        {
            updates.Add(Builders<BsonDocument>.Update.Set(FieldAlbumArt, albumArt));
        }

        await Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", DocId(userId, artistName, albumName)),
            Builders<BsonDocument>.Update.Combine(updates),
            new UpdateOptions { IsUpsert = true });
    }

    public Task Clear(string userId, string artistName, string albumName) =>
        Collection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", DocId(userId, artistName, albumName)));

    public async Task<HashSet<string>> GetDecidedKeys(string userId)
    {
        var cursor = await Collection.FindAsync(
            Builders<BsonDocument>.Filter.Eq(FieldUserId, userId),
            new FindOptions<BsonDocument>
            {
                Projection = Builders<BsonDocument>.Projection.Include(FieldArtist).Include(FieldAlbum),
            });

        var keys = new HashSet<string>();
        foreach (var doc in await cursor.ToListAsync())
        {
            var artist = doc.TryGetValue(FieldArtist, out var a) && !a.IsBsonNull ? a.AsString : null;
            var album = doc.TryGetValue(FieldAlbum, out var al) && !al.IsBsonNull ? al.AsString : null;
            if (artist != null && album != null)
            {
                keys.Add(AlbumRatingKey.For(artist, album));
            }
        }
        return keys;
    }

    public Task<AlbumRating[]> GetRated(string userId) =>
        Query(Builders<BsonDocument>.Filter.Eq(FieldUserId, userId));

    public Task<AlbumRating[]> GetLiked(string userId) =>
        Query(Builders<BsonDocument>.Filter.Eq(FieldUserId, userId)
              & Builders<BsonDocument>.Filter.Eq(FieldStatus, StatusLiked));

    public Task<AlbumRating[]> GetAllLiked() =>
        Query(Builders<BsonDocument>.Filter.Eq(FieldStatus, StatusLiked));

    private async Task<AlbumRating[]> Query(FilterDefinition<BsonDocument> filter)
    {
        var cursor = await Collection.FindAsync(filter, new FindOptions<BsonDocument>
        {
            Sort = Builders<BsonDocument>.Sort.Descending(FieldDecidedAt),
        });
        return (await cursor.ToListAsync()).Select(ToRating).ToArray();
    }

    private static AlbumRating ToRating(BsonDocument doc)
    {
        var artist = doc.TryGetValue(FieldArtist, out var a) && !a.IsBsonNull ? a.AsString : "";
        var album = doc.TryGetValue(FieldAlbum, out var al) && !al.IsBsonNull ? al.AsString : "";
        var art = doc.TryGetValue(FieldAlbumArt, out var art2) && !art2.IsBsonNull ? art2.AsString : null;
        var status = doc.TryGetValue(FieldStatus, out var s) && !s.IsBsonNull
            && Enum.TryParse<DiscoveryStatus>(s.AsString, out var parsed)
            ? parsed
            : DiscoveryStatus.Pending;
        return new AlbumRating(new ArtistKey(artist), new AlbumKey(album), art, status);
    }
}
