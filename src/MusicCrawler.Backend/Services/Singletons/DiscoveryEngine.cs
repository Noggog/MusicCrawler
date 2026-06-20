using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// The seam the periodic <c>QueueReplenishService</c> drives — a per-user, additive top-up of the
/// recommendation queue. Implemented by <see cref="DiscoveryEngine"/>; extracted so the background
/// service can be unit-tested without constructing the whole engine.
/// </summary>
public interface IQueueReplenisher
{
    /// <summary>Gently grows (and refreshes stale edges for) the user's queue without clearing pending.</summary>
    Task TopUp(string userId);
}

/// <summary>
/// The discovery loop. Surfaces three kinds of things to react to — new recommended artists, owned
/// artists not yet rated, and albums missing from <em>liked</em> artists — and steers a per-user walk
/// through the similarity graph by the user's verdicts.
///
/// There is no separate "seed" concept: the frontier is simply the user's <em>Liked</em> artists
/// (owned taste anchors and approved recommendations alike). A thumbs-up on an artist grows the
/// frontier from it (and, if it isn't owned, queues it to buy); a thumbs-down prunes. Albums are
/// rated independently — a liked missing album joins the buy list and drops off once acquired.
/// Recommendations never re-add an artist that's owned, already-decided, or the frontier itself, so
/// the frontier only moves outward.
/// </summary>
public class DiscoveryEngine : IQueueReplenisher
{
    private readonly IUserQueueRepo _queue;
    private readonly IRelatedArtistReader _related;
    private readonly ILibraryProvider _library;
    private readonly IArtistCatalogRepo _catalog;
    private readonly IMissingAlbumRepo _missing;
    private readonly IUserAlbumRatingRepo _albumRatings;
    private readonly MissingAlbumRefresher _albumRefresher;
    private readonly ILogger<DiscoveryEngine> _logger;

    public DiscoveryEngine(
        IUserQueueRepo queue,
        IRelatedArtistReader related,
        ILibraryProvider library,
        IArtistCatalogRepo catalog,
        IMissingAlbumRepo missing,
        IUserAlbumRatingRepo albumRatings,
        MissingAlbumRefresher albumRefresher,
        ILogger<DiscoveryEngine> logger)
    {
        _queue = queue;
        _related = related;
        _library = library;
        _catalog = catalog;
        _missing = missing;
        _albumRatings = albumRatings;
        _albumRefresher = albumRefresher;
        _logger = logger;
    }

    // ---- Feed ----

    /// <summary>A paged feed of one category alone.</summary>
    public async Task<DiscoveryFeedPage> GetFeed(string userId, FeedKind kind, int page, int pageSize)
    {
        var all = await ItemsForKind(userId, kind);
        var items = all.Skip(page * pageSize).Take(pageSize).ToArray();
        return new DiscoveryFeedPage(kind, items, page, pageSize, all.Count);
    }

    /// <summary>
    /// A single mixed feed across the selected categories. Each category's items are shuffled (a
    /// stable, <paramref name="seed"/>-driven shuffle so paging is consistent) and then round-robin
    /// interleaved, so the user sees a balanced, varied mix — a recommendation, then a missing album,
    /// then an owned artist to rate — rather than 70 of one kind before any of another.
    /// </summary>
    public async Task<DiscoveryFeedPage> GetMixedFeed(
        string userId, IReadOnlyList<FeedKind> kinds, int page, int pageSize, int seed)
    {
        var lists = new List<List<FeedItem>>();
        foreach (var kind in kinds.Distinct())
        {
            var items = await ItemsForKind(userId, kind);
            // Offset the seed per kind so different categories don't shuffle into lockstep.
            Shuffle(items, seed ^ ((int)kind * 2654435761u).GetHashCode());
            lists.Add(items);
        }

        var mixed = RoundRobin(lists);
        var pageItems = mixed.Skip(page * pageSize).Take(pageSize).ToArray();
        // Page.Kind is meaningless for a mix; each item carries its own kind. Use the first requested.
        var headerKind = kinds.Count > 0 ? kinds[0] : FeedKind.RecommendedArtist;
        return new DiscoveryFeedPage(headerKind, pageItems, page, pageSize, mixed.Count);
    }

    /// <summary>The full (unpaged) list of feed items for one category.</summary>
    private Task<List<FeedItem>> ItemsForKind(string userId, FeedKind kind) => kind switch
    {
        FeedKind.RecommendedArtist => RecommendedItems(userId),
        FeedKind.LibraryArtist => LibraryItems(userId),
        FeedKind.RecommendedLibraryArtist => LibraryItemsBySection(userId, recommended: true),
        FeedKind.SeedLibraryArtist => LibraryItemsBySection(userId, recommended: false),
        FeedKind.MissingAlbum => MissingAlbumItems(userId),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown feed kind"),
    };

    private async Task<List<FeedItem>> RecommendedItems(string userId)
    {
        await EnsureQueue(userId);
        // Pull the whole pending set (modest in practice); paging/mixing happens above.
        var pageData = await _queue.GetPending(userId, 0, int.MaxValue);
        return pageData.Items
            .Select(c => new FeedItem(FeedKind.RecommendedArtist, c.Artist, null, c.ImageUrl, c.Score, c.Sources, null))
            .ToList();
    }

    private async Task<List<FeedItem>> LibraryItems(string userId)
    {
        // Owned artists the user hasn't thumbed yet — computed as catalog minus already-decided, so
        // there's nothing to precompute or keep in sync.
        var decided = await _queue.GetDecidedArtists(userId);
        return (await _library.GetAllArtistMetadata())
            .Where(a => !decided.Contains(a.ArtistKey.ArtistName))
            .OrderBy(a => a.ArtistKey.ArtistName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new FeedItem(FeedKind.LibraryArtist, a.ArtistKey, null, a.ArtistImageUrl, 0, Array.Empty<string>(), null))
            .ToList();
    }

    /// <summary>
    /// One section of the owned-unrated artists, split by whether a <em>liked</em> artist recommends
    /// them. <paramref name="recommended"/>=true yields artists the frontier already vouches for
    /// (sorted by how many liked artists point at them, with that provenance attached);
    /// =false yields the rest — fresh artists to rate that would seed new recommendations.
    /// </summary>
    private async Task<List<FeedItem>> LibraryItemsBySection(string userId, bool recommended)
    {
        var decided = await _queue.GetDecidedArtists(userId);
        var owned = (await _library.GetAllArtistMetadata())
            .Where(a => !decided.Contains(a.ArtistKey.ArtistName))
            .ToList();
        var byLiked = await OwnedRecommendedByLiked(userId, owned);

        if (recommended)
        {
            return owned
                .Where(a => byLiked.ContainsKey(a.ArtistKey.ArtistName))
                .OrderByDescending(a => byLiked[a.ArtistKey.ArtistName].Count)
                .ThenBy(a => a.ArtistKey.ArtistName, StringComparer.OrdinalIgnoreCase)
                .Select(a => new FeedItem(
                    FeedKind.RecommendedLibraryArtist, a.ArtistKey, null, a.ArtistImageUrl,
                    byLiked[a.ArtistKey.ArtistName].Count, byLiked[a.ArtistKey.ArtistName].ToArray(), null))
                .ToList();
        }

        return owned
            .Where(a => !byLiked.ContainsKey(a.ArtistKey.ArtistName))
            .OrderBy(a => a.ArtistKey.ArtistName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new FeedItem(
                FeedKind.SeedLibraryArtist, a.ArtistKey, null, a.ArtistImageUrl, 0, Array.Empty<string>(), null))
            .ToList();
    }

    /// <summary>
    /// Of the given owned artists, those at least one <em>liked</em> artist recommends — mapped to the
    /// liked artists that point at them (provenance). Computed live from the similarity graph (the
    /// same edges the frontier expansion walks), so there's nothing to precompute or keep in sync.
    /// Liked artists' related edges are already cached from expansion, so this is graph reads, no fetch.
    /// </summary>
    private async Task<Dictionary<string, List<string>>> OwnedRecommendedByLiked(
        string userId, IReadOnlyList<ArtistMetadata> owned)
    {
        var ownedSet = owned.Select(a => a.ArtistKey.ArtistName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var likedArtist in await _queue.GetLikedArtistNames(userId))
        {
            var unified = await _related.GetRelated(new ArtistKey(likedArtist));
            foreach (var rel in unified.Related)
            {
                var name = rel.ArtistKey.ArtistName;
                if (!ownedSet.Contains(name))
                {
                    continue;
                }

                if (!sources.TryGetValue(name, out var srcs))
                {
                    sources[name] = srcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                srcs.Add(likedArtist);
            }
        }

        return sources.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<FeedItem>> MissingAlbumItems(string userId)
    {
        // Gap-fill only for artists the user has thumbed up (the frontier) — not every owned artist.
        // A fresh user with no likes sees no missing albums until they like a band.
        var liked = (await _queue.GetLikedArtistNames(userId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (liked.Count == 0)
        {
            return new List<FeedItem>();
        }

        var decided = await _albumRatings.GetDecidedKeys(userId);
        return (await _missing.GetAll())
            .Where(m => liked.Contains(m.Artist.ArtistName))
            .Where(m => !decided.Contains(AlbumRatingKey.For(m.Artist.ArtistName, m.Album.AlbumName)))
            .Select(m => new FeedItem(
                FeedKind.MissingAlbum, m.Artist, m.Album.AlbumName, m.AlbumArt, 0, Array.Empty<string>(), m.DeezerAlbumId))
            .ToList();
    }

    /// <summary>
    /// On-demand: a brand-new (non-owned) liked artist's albums, so the discover→acquire loop can act
    /// on a fresh discovery rather than just wishlisting the artist. Pulls the artist's Deezer
    /// discography, persists it into the global missing-album store (so a liked album carries its
    /// <see cref="MissingAlbum.DeezerAlbumId"/> through reconcile to the downloader — without that row
    /// the album would be un-downloadable), and returns the not-yet-decided ones as missing-album feed
    /// items to surface inline under the just-rated card. Whatever Plex already owns for the artist is
    /// diffed out, so a partly-owned artist only surfaces its gaps.
    /// </summary>
    public async Task<IReadOnlyList<FeedItem>> ArtistAlbums(string userId, string artistName)
    {
        var ownedAlbums = await _catalog.GetOwnedAlbums();
        var owned = ownedAlbums.TryGetValue(artistName, out var set)
            ? (IEnumerable<string>)set
            : Array.Empty<string>();

        var rows = await _albumRefresher.RefreshOne(new ArtistKey(artistName), owned);
        var decided = await _albumRatings.GetDecidedKeys(userId);
        return rows
            .Where(m => !decided.Contains(AlbumRatingKey.For(m.Artist.ArtistName, m.Album.AlbumName)))
            .Select(m => new FeedItem(
                FeedKind.MissingAlbum, m.Artist, m.Album.AlbumName, m.AlbumArt, 0, Array.Empty<string>(), m.DeezerAlbumId))
            .ToList();
    }

    /// <summary>
    /// An owned artist's full Deezer discography for the Artists-page drill-down: every LP flagged with
    /// whether the library owns it, missing ones overlaid with the user's verdict (queued/dismissed/
    /// snoozed) so the listing matches the to-buy list. One Deezer call per expand; owned albums sort
    /// first. Pulling the discography also refreshes the persisted missing-album rows for the artist.
    /// </summary>
    public async Task<IReadOnlyList<ArtistAlbumItem>> ArtistDiscography(string userId, string artistName)
    {
        var ownedAlbums = await _catalog.GetOwnedAlbums();
        var owned = ownedAlbums.TryGetValue(artistName, out var set)
            ? (IReadOnlyCollection<string>)set
            : Array.Empty<string>();

        var albums = await _albumRefresher.Discography(new ArtistKey(artistName), owned);

        // The user's verdicts on this artist's albums, keyed like the album-rating store, so a missing
        // album already queued/dismissed/snoozed shows that state instead of fresh action buttons.
        var verdicts = (await _albumRatings.GetRated(userId))
            .Where(r => string.Equals(r.Artist.ArtistName, artistName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(r => AlbumRatingKey.For(r.Artist.ArtistName, r.Album.AlbumName), r => r.Status);

        var artist = new ArtistKey(artistName);
        return albums
            .OrderByDescending(a => a.Owned)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .Select(a =>
            {
                DiscoveryStatus? verdict = !a.Owned
                    && verdicts.TryGetValue(AlbumRatingKey.For(artistName, a.Title), out var v)
                    ? v
                    : null;
                return new ArtistAlbumItem(artist, a.Title, a.CoverUrl, a.DeezerAlbumId, a.Owned, verdict);
            })
            .ToList();
    }

    /// <summary>In-place Fisher–Yates shuffle with a fixed seed (so the order is stable across pages).</summary>
    private static void Shuffle(List<FeedItem> items, int seed)
    {
        var rng = new Random(seed);
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Interleaves the lists one element at a time (list0[0], list1[0], …, list0[1], …).</summary>
    private static List<FeedItem> RoundRobin(List<List<FeedItem>> lists)
    {
        var result = new List<FeedItem>(lists.Sum(l => l.Count));
        var max = lists.Count == 0 ? 0 : lists.Max(l => l.Count);
        for (var i = 0; i < max; i++)
        {
            foreach (var list in lists)
            {
                if (i < list.Count)
                {
                    result.Add(list[i]);
                }
            }
        }
        return result;
    }

    // ---- Rating ----

    /// <summary>
    /// Thumbs an artist. A like records the verdict and grows the frontier from it (queuing it to buy
    /// too, if it isn't owned); a dislike just prunes — recording the verdict is enough.
    /// </summary>
    public async Task RateArtist(string userId, string artistName, DiscoveryStatus status)
    {
        var rated = await _queue.Rate(userId, artistName, status, imageUrl: null);
        if (status == DiscoveryStatus.Liked)
        {
            await ExpandFrom(userId, new[] { artistName }, depth: (rated?.Depth ?? 0) + 1);
        }
    }

    /// <summary>
    /// Snoozes an artist for <paramref name="duration"/> — hides it from the feed and resurfaces it
    /// when the window lapses. Unlike a like, it never grows the frontier (a snooze is "not now", not
    /// "yes"); unlike a dislike, it isn't permanent.
    /// </summary>
    public Task SnoozeArtist(string userId, string artistName, TimeSpan duration) =>
        _queue.Snooze(userId, artistName, DateTimeOffset.UtcNow + duration, imageUrl: null);

    /// <summary>
    /// Snoozes a missing album for <paramref name="duration"/> — hides it from the missing-albums feed
    /// and resurfaces it when the window lapses. The album analogue of <see cref="SnoozeArtist"/>.
    /// </summary>
    public Task SnoozeAlbum(string userId, string artistName, string albumName, string? albumArt, TimeSpan duration) =>
        _albumRatings.Snooze(userId, artistName, albumName, albumArt, DateTimeOffset.UtcNow + duration);

    /// <summary>Thumbs a missing album: like = queue to buy, dislike = not interested.</summary>
    public Task RateAlbum(string userId, string artistName, string albumName, string? albumArt, DiscoveryStatus status) =>
        _albumRatings.Rate(userId, artistName, albumName, albumArt, status);

    /// <summary>Clears an artist's verdict, returning it to the feed (recommended or library).</summary>
    public Task ClearArtistRating(string userId, string artistName) =>
        _queue.ClearVerdict(userId, artistName);

    /// <summary>Clears an album's verdict, returning it to the missing-albums feed.</summary>
    public Task ClearAlbumRating(string userId, string artistName, string albumName) =>
        _albumRatings.Clear(userId, artistName, albumName);

    /// <summary>Discards the pending recommendations and rebuilds them from the current liked artists.</summary>
    public async Task Rebuild(string userId)
    {
        await _queue.DeletePending(userId);
        await ExpandFrom(userId, await _queue.GetLikedArtistNames(userId), depth: 1);
    }

    /// <summary>
    /// A gentle, additive top-up of the queue for the periodic replenisher — re-expands from the
    /// liked artists <em>without</em> clearing pending (so it never reshuffles a user mid-swipe). The
    /// upsert is idempotent, and the expansion naturally refetches similarity edges that have gone
    /// stale, so one pass both grows the frontier and refreshes the graph.
    /// </summary>
    public async Task TopUp(string userId) =>
        await ExpandFrom(userId, await _queue.GetLikedArtistNames(userId), depth: 1);

    // ---- Review ----

    /// <summary>
    /// Every rating the user has made, for the review page. Album ratings whose album has since been
    /// acquired are dropped — once it exists, it's no longer interesting.
    /// </summary>
    public async Task<RatedItem[]> GetRatings(string userId)
    {
        var owned = (await _library.GetAllArtistMetadata())
            .Select(a => a.ArtistKey.ArtistName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownedAlbums = await _catalog.GetOwnedAlbums();

        var artistItems = (await _queue.GetRated(userId))
            .Select(r => new RatedItem(
                owned.Contains(r.Artist.ArtistName) ? FeedKind.LibraryArtist : FeedKind.RecommendedArtist,
                r.Artist, null, r.ImageUrl, r.Status, r.SnoozeUntil));

        var albumItems = (await _albumRatings.GetRated(userId))
            .Where(r => !AlbumIsOwned(ownedAlbums, r.Artist.ArtistName, r.Album.AlbumName))
            .Select(r => new RatedItem(
                FeedKind.MissingAlbum, r.Artist, r.Album.AlbumName, r.AlbumArt, r.Status, r.SnoozeUntil));

        return artistItems.Concat(albumItems).ToArray();
    }

    private static bool AlbumIsOwned(Dictionary<string, HashSet<string>> ownedAlbums, string artist, string album) =>
        ownedAlbums.TryGetValue(artist, out var set) && set.Contains(album);

    // ---- Frontier expansion ----

    /// <summary>Builds the initial recommendation queue from the liked artists, only when empty.</summary>
    private async Task EnsureQueue(string userId)
    {
        if (await _queue.CountPending(userId) > 0)
        {
            return;
        }

        var liked = await _queue.GetLikedArtistNames(userId);
        if (liked.Length == 0)
        {
            return;
        }

        await ExpandFrom(userId, liked, depth: 1);
    }

    /// <summary>
    /// Walks one step out from <paramref name="frontier"/>: pulls each frontier artist's related
    /// artists from the similarity graph (ingesting from the source on a miss), aggregates them so a
    /// candidate several frontier artists agree on accrues score and provenance, drops anything
    /// owned/already-decided, and upserts the survivors as pending candidates.
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
