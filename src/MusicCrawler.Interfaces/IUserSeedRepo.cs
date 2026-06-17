namespace MusicCrawler.Interfaces;

/// <summary>
/// Per-user "seed" artists — the library artists a user has marked as taste anchors that the
/// recommendation engine grows from. Stored per user; an artist is referenced by name (the same
/// key the catalog and similarity graph use).
/// </summary>
public interface IUserSeedRepo
{
    Task<string[]> GetSeeds(string userId);
    Task AddSeed(string userId, string artistName);
    Task RemoveSeed(string userId, string artistName);
}
