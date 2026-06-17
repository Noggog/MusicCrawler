namespace MusicCrawler.Interfaces;

/// <summary>
/// Per-user discovery queue: the precomputed swipe candidates the tree-search engine grows from a
/// user's seeds. One document per (user, artist); a candidate is Pending until swiped, then Liked
/// (kept as the "to buy" wishlist) or Disliked (pruned). Distinct from the global similarity graph
/// (<see cref="IRelatedArtistRepo"/>) — this is the user's personal walk through it.
/// </summary>
public interface IUserQueueRepo
{
    /// <summary>
    /// Adds new pending candidates or, for ones already pending, bumps their score and merges in
    /// the new provenance sources — atomically. Candidates already Liked/Disliked must be excluded
    /// by the caller; this never resurrects a decided artist.
    /// </summary>
    Task UpsertCandidates(string userId, IReadOnlyList<DiscoveryCandidate> candidates);

    /// <summary>The artist names this user has already Liked or Disliked — the expansion exclusion set.</summary>
    Task<HashSet<string>> GetDecidedArtists(string userId);

    /// <summary>A score-ranked page of this user's pending candidates, plus the total pending count.</summary>
    Task<DiscoveryPage> GetPending(string userId, int page, int pageSize);

    /// <summary>Count of pending candidates — used to decide whether the queue needs an initial build.</summary>
    Task<long> CountPending(string userId);

    /// <summary>
    /// Records a swipe verdict, returning the affected candidate (for the engine to read its depth
    /// when growing the frontier), or null if no such candidate exists.
    /// </summary>
    Task<DiscoveryCandidate?> SetVerdict(string userId, string artistName, DiscoveryStatus status);

    /// <summary>The user's Liked candidates — the "to buy" wishlist, newest decision first.</summary>
    Task<DiscoveryCandidate[]> GetLiked(string userId);

    /// <summary>Clears pending candidates (keeps Liked/Disliked) so the queue can be rebuilt from seeds.</summary>
    Task DeletePending(string userId);
}
