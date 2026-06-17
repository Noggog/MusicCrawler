using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// The discovery loop: walks the similarity graph from a user's seeds to build a personal,
/// score-ranked swipe queue, then steers that walk by the user's verdicts. A thumbs-up makes the
/// liked artist a new frontier node (its neighbours get queued) and keeps it on the wishlist; a
/// thumbs-down prunes (no expansion, never shown again). Expansion never re-adds an artist that's
/// already in the library, a seed, or already decided — so the frontier only ever moves outward.
/// </summary>
public class DiscoveryEngine
{
    private readonly IUserQueueRepo _queue;
    private readonly IUserSeedRepo _seeds;
    private readonly IRelatedArtistReader _related;
    private readonly ILibraryProvider _library;
    private readonly ILogger<DiscoveryEngine> _logger;

    public DiscoveryEngine(
        IUserQueueRepo queue,
        IUserSeedRepo seeds,
        IRelatedArtistReader related,
        ILibraryProvider library,
        ILogger<DiscoveryEngine> logger)
    {
        _queue = queue;
        _seeds = seeds;
        _related = related;
        _library = library;
        _logger = logger;
    }

    /// <summary>A ranked page of the user's queue, building it from seeds first if it's empty.</summary>
    public async Task<DiscoveryPage> GetQueue(string userId, int page, int pageSize)
    {
        await EnsureQueue(userId);
        return await _queue.GetPending(userId, page, pageSize);
    }

    /// <summary>Discards the pending queue and rebuilds it from the current seeds (e.g. after the
    /// user edits their seeds). Liked/Disliked verdicts are kept, so the wishlist and prunes survive.</summary>
    public async Task<DiscoveryPage> Rebuild(string userId, int page, int pageSize)
    {
        await _queue.DeletePending(userId);
        await ExpandFrom(userId, await _seeds.GetSeeds(userId), depth: 1);
        return await _queue.GetPending(userId, page, pageSize);
    }

    /// <summary>Thumbs-up: record the like, then grow the frontier from the liked artist.</summary>
    public async Task Like(string userId, string artistName)
    {
        var liked = await _queue.SetVerdict(userId, artistName, DiscoveryStatus.Liked);
        await ExpandFrom(userId, new[] { artistName }, depth: (liked?.Depth ?? 0) + 1);
    }

    /// <summary>Thumbs-down: prune. Recording the verdict is enough — no expansion.</summary>
    public Task Dislike(string userId, string artistName) =>
        _queue.SetVerdict(userId, artistName, DiscoveryStatus.Disliked);

    /// <summary>The user's "to buy" wishlist (every Liked artist).</summary>
    public Task<DiscoveryCandidate[]> GetPurchases(string userId) =>
        _queue.GetLiked(userId);

    /// <summary>Builds the initial queue from seeds, but only when it's currently empty.</summary>
    private async Task EnsureQueue(string userId)
    {
        if (await _queue.CountPending(userId) > 0)
        {
            return;
        }

        var seeds = await _seeds.GetSeeds(userId);
        if (seeds.Length == 0)
        {
            return;
        }

        await ExpandFrom(userId, seeds, depth: 1);
    }

    /// <summary>
    /// Walks one step out from <paramref name="frontier"/>: pulls each frontier artist's related
    /// artists from the similarity graph (ingesting from the source on a miss), aggregates them so
    /// a candidate several frontier artists agree on accrues score and provenance, drops anything
    /// owned/seeded/already-decided, and upserts the survivors as pending candidates.
    /// </summary>
    private async Task ExpandFrom(string userId, IReadOnlyList<string> frontier, int depth)
    {
        if (frontier.Count == 0)
        {
            return;
        }

        var library = (await _library.GetAllArtistMetadata())
            .Select(a => a.ArtistKey.ArtistName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seeds = new HashSet<string>(await _seeds.GetSeeds(userId), StringComparer.OrdinalIgnoreCase);
        var decided = await _queue.GetDecidedArtists(userId);

        var aggregated = new Dictionary<string, Aggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var frontierArtist in frontier)
        {
            var unified = await _related.GetRelated(new ArtistKey(frontierArtist));
            foreach (var candidate in unified.Related)
            {
                var name = candidate.ArtistKey.ArtistName;
                if (string.IsNullOrWhiteSpace(name)
                    || name.Equals(frontierArtist, StringComparison.OrdinalIgnoreCase)
                    || library.Contains(name)
                    || seeds.Contains(name)
                    || decided.Contains(name))
                {
                    continue;
                }

                if (!aggregated.TryGetValue(name, out var agg))
                {
                    agg = new Aggregate(name, candidate.ImageUrl, depth);
                    aggregated[name] = agg;
                }

                // One point per frontier artist that points here, plus a small bump for candidates
                // multiple sources (Deezer, …) independently recommend.
                agg.Score += 1.0 + 0.25 * candidate.Sources.Count;
                agg.Sources.Add(frontierArtist);
                agg.ImageUrl ??= candidate.ImageUrl;
            }
        }

        if (aggregated.Count == 0)
        {
            _logger.LogInformation(
                "Discovery expansion for {User} from {FrontierCount} artist(s) yielded no new candidates",
                userId, frontier.Count);
            return;
        }

        var candidates = aggregated.Values
            .Select(a => new DiscoveryCandidate(new ArtistKey(a.Name), a.ImageUrl, a.Score, a.Sources.ToArray(), a.Depth))
            .ToArray();

        await _queue.UpsertCandidates(userId, candidates);
        _logger.LogInformation(
            "Discovery expansion for {User} queued/bumped {Count} candidate(s) at depth {Depth}",
            userId, candidates.Length, depth);
    }

    private sealed class Aggregate
    {
        public Aggregate(string name, string? imageUrl, int depth)
        {
            Name = name;
            ImageUrl = imageUrl;
            Depth = depth;
        }

        public string Name { get; }
        public string? ImageUrl { get; set; }
        public int Depth { get; }
        public double Score { get; set; }
        public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
