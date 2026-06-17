using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Interfaces;

namespace MusicCrawler.MongoDB.Services.Data;

/// <summary>
/// Mongo-backed per-user discovery queue. One document per (user, artist) in the "userQueue"
/// collection, keyed "{userId}:{artist}", in the clean BsonDocument-mapping style of
/// <see cref="RelatedArtistRepo"/>. Status drives the swipe loop; score ranks pending candidates.
/// </summary>
public class UserQueueRepo : IUserQueueRepo
{
    private const string CollectionName = "userQueue";
    private const string FieldUserId = "userId";
    private const string FieldArtist = "artist";
    private const string FieldImageUrl = "imageUrl";
    private const string FieldStatus = "status";
    private const string FieldScore = "score";
    private const string FieldSources = "sources";
    private const string FieldDepth = "depth";
    private const string FieldAddedAt = "addedAt";
    private const string FieldDecidedAt = "decidedAt";

    private static readonly string StatusPending = DiscoveryStatus.Pending.ToString();
    private static readonly string StatusLiked = DiscoveryStatus.Liked.ToString();

    private readonly IMongoDbProvider _mongoDbProvider;

    public UserQueueRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    private static string DocId(string userId, string artistName) => $"{userId}:{artistName}";

    public async Task UpsertCandidates(string userId, IReadOnlyList<DiscoveryCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.UtcDateTime;
        var models = new List<WriteModel<BsonDocument>>(candidates.Count);

        foreach (var c in candidates)
        {
            var name = c.Artist.ArtistName;

            // $setOnInsert seeds immutable fields on first sight; $inc/$addToSet/$min merge a repeat
            // sighting into the existing pending doc (bump score, accrue provenance, shorten depth).
            // The caller filters out decided artists, so the only doc this _id can match is a pending
            // one — never a Liked/Disliked one, so a thumbs-down stays pruned.
            var updates = new List<UpdateDefinition<BsonDocument>>
            {
                Builders<BsonDocument>.Update.SetOnInsert(FieldUserId, userId),
                Builders<BsonDocument>.Update.SetOnInsert(FieldArtist, name),
                Builders<BsonDocument>.Update.SetOnInsert(FieldStatus, StatusPending),
                Builders<BsonDocument>.Update.SetOnInsert(FieldAddedAt, now),
                Builders<BsonDocument>.Update.Inc(FieldScore, c.Score),
                Builders<BsonDocument>.Update.Min(FieldDepth, c.Depth),
                Builders<BsonDocument>.Update.AddToSetEach(FieldSources, c.Sources),
            };

            // Fill the image when this sighting has one; don't clobber an existing image with null.
            if (c.ImageUrl != null)
            {
                updates.Add(Builders<BsonDocument>.Update.Set(FieldImageUrl, c.ImageUrl));
            }

            models.Add(new UpdateOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("_id", DocId(userId, name)),
                Builders<BsonDocument>.Update.Combine(updates))
            {
                IsUpsert = true,
            });
        }

        await Collection.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false });
    }

    public async Task<HashSet<string>> GetDecidedArtists(string userId)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(FieldUserId, userId)
                     & Builders<BsonDocument>.Filter.Ne(FieldStatus, StatusPending);
        var cursor = await Collection.FindAsync(
            filter,
            new FindOptions<BsonDocument> { Projection = Builders<BsonDocument>.Projection.Include(FieldArtist) });
        var docs = await cursor.ToListAsync();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
        {
            if (doc.TryGetValue(FieldArtist, out var a) && !a.IsBsonNull)
            {
                names.Add(a.AsString);
            }
        }

        return names;
    }

    public async Task<DiscoveryPage> GetPending(string userId, int page, int pageSize)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(FieldUserId, userId)
                     & Builders<BsonDocument>.Filter.Eq(FieldStatus, StatusPending);

        var total = await Collection.CountDocumentsAsync(filter);

        // Highest score first; ties broken by oldest-added so the order is stable across pages.
        var sort = Builders<BsonDocument>.Sort.Descending(FieldScore).Ascending(FieldAddedAt);
        var cursor = await Collection.FindAsync(filter, new FindOptions<BsonDocument>
        {
            Sort = sort,
            Skip = page * pageSize,
            Limit = pageSize,
        });

        var items = (await cursor.ToListAsync()).Select(ToCandidate).ToArray();
        return new DiscoveryPage(items, page, pageSize, total);
    }

    public async Task<long> CountPending(string userId)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(FieldUserId, userId)
                     & Builders<BsonDocument>.Filter.Eq(FieldStatus, StatusPending);
        return await Collection.CountDocumentsAsync(filter);
    }

    public async Task<DiscoveryCandidate?> SetVerdict(string userId, string artistName, DiscoveryStatus status)
    {
        var update = Builders<BsonDocument>.Update
            .Set(FieldStatus, status.ToString())
            .Set(FieldDecidedAt, DateTimeOffset.UtcNow.UtcDateTime);

        var doc = await Collection.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", DocId(userId, artistName)),
            update,
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After });

        return doc == null ? null : ToCandidate(doc);
    }

    public async Task<DiscoveryCandidate[]> GetLiked(string userId)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(FieldUserId, userId)
                     & Builders<BsonDocument>.Filter.Eq(FieldStatus, StatusLiked);
        var cursor = await Collection.FindAsync(filter, new FindOptions<BsonDocument>
        {
            Sort = Builders<BsonDocument>.Sort.Descending(FieldDecidedAt),
        });
        return (await cursor.ToListAsync()).Select(ToCandidate).ToArray();
    }

    public Task DeletePending(string userId) =>
        Collection.DeleteManyAsync(
            Builders<BsonDocument>.Filter.Eq(FieldUserId, userId)
            & Builders<BsonDocument>.Filter.Eq(FieldStatus, StatusPending));

    private static DiscoveryCandidate ToCandidate(BsonDocument doc)
    {
        var artist = doc.TryGetValue(FieldArtist, out var a) && !a.IsBsonNull ? a.AsString : "";
        var imageUrl = doc.TryGetValue(FieldImageUrl, out var img) && !img.IsBsonNull ? img.AsString : null;
        var score = doc.TryGetValue(FieldScore, out var s) && s.IsNumeric ? s.ToDouble() : 0;
        var depth = doc.TryGetValue(FieldDepth, out var d) && d.IsNumeric ? d.ToInt32() : 0;

        var sources = new List<string>();
        if (doc.TryGetValue(FieldSources, out var src) && src.IsBsonArray)
        {
            sources.AddRange(src.AsBsonArray.Where(x => !x.IsBsonNull).Select(x => x.AsString));
        }

        return new DiscoveryCandidate(new ArtistKey(artist), imageUrl, score, sources, depth);
    }
}
