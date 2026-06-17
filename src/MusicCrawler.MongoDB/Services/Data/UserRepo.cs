using MongoDB.Bson;
using MongoDB.Driver;
using MusicCrawler.Interfaces;

namespace MusicCrawler.MongoDB.Services.Data;

/// <summary>
/// Mongo-backed user store. One document per user in the "users" collection, keyed by OIDC subject.
/// </summary>
public class UserRepo : IUserRepo
{
    private const string CollectionName = "users";
    private const string FieldUsername = "username";
    private const string FieldEmail = "email";
    private const string FieldDisplayName = "displayName";
    private const string FieldFirstSeenAt = "firstSeenAt";
    private const string FieldLastLoginAt = "lastLoginAt";

    private readonly IMongoDbProvider _mongoDbProvider;

    public UserRepo(IMongoDbProvider mongoDbProvider)
    {
        _mongoDbProvider = mongoDbProvider;
    }

    private IMongoCollection<BsonDocument> Collection =>
        _mongoDbProvider.database.GetCollection<BsonDocument>(CollectionName);

    public async Task UpsertOnLogin(AppUser user)
    {
        var update = Builders<BsonDocument>.Update
            .Set(FieldUsername, (BsonValue?)user.Username ?? BsonNull.Value)
            .Set(FieldEmail, (BsonValue?)user.Email ?? BsonNull.Value)
            .Set(FieldDisplayName, (BsonValue?)user.DisplayName ?? BsonNull.Value)
            .Set(FieldLastLoginAt, user.LastLoginAt.UtcDateTime)
            // First-seen is written only on the initial insert, never overwritten on later logins.
            .SetOnInsert(FieldFirstSeenAt, user.FirstSeenAt.UtcDateTime);

        await Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", user.Subject),
            update,
            new UpdateOptions { IsUpsert = true });
    }

    public async Task<AppUser?> Get(string subject)
    {
        var cursor = await Collection.FindAsync(Builders<BsonDocument>.Filter.Eq("_id", subject));
        var doc = await cursor.FirstOrDefaultAsync();
        return doc == null ? null : ToAppUser(doc);
    }

    private static AppUser ToAppUser(BsonDocument doc)
    {
        string? Str(string field) =>
            doc.TryGetValue(field, out var v) && !v.IsBsonNull ? v.AsString : null;

        DateTimeOffset Date(string field) =>
            doc.TryGetValue(field, out var v) && v.IsValidDateTime
                ? new DateTimeOffset(v.ToUniversalTime(), TimeSpan.Zero)
                : default;

        return new AppUser(
            Subject: doc["_id"].AsString,
            Username: Str(FieldUsername),
            Email: Str(FieldEmail),
            DisplayName: Str(FieldDisplayName),
            FirstSeenAt: Date(FieldFirstSeenAt),
            LastLoginAt: Date(FieldLastLoginAt));
    }
}
