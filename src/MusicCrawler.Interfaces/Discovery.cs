using System.Text.Json.Serialization;

namespace MusicCrawler.Interfaces;

/// <summary>Where a candidate sits in the per-user swipe loop.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscoveryStatus
{
    /// <summary>Awaiting a swipe — eligible to be shown.</summary>
    Pending,

    /// <summary>Thumbs-up: grows the frontier and lands on the "to buy" wishlist.</summary>
    Liked,

    /// <summary>Thumbs-down: pruned — never shown or expanded again.</summary>
    Disliked,

    /// <summary>
    /// Temporarily hidden until <c>snoozeUntil</c> passes, then it resurfaces as pending (lazily, on
    /// the next read). Counts as decided while unexpired (not re-added by expansion); once expired it
    /// drops back out of the decided set so the frontier may re-touch it.
    /// </summary>
    Snoozed,
}

/// <summary>
/// One artist in a user's discovery queue. <paramref name="Score"/> ranks the queue (higher =
/// shown sooner); it accrues each time a frontier artist points here, so candidates several of
/// your tastes agree on float to the top. <paramref name="Sources"/> is the provenance shown in
/// the UI ("via boygenius, Snail Mail") — the frontier artists that recommended this one.
/// <paramref name="Depth"/> is the graph distance from a seed (seeds' neighbours = 1).
/// </summary>
public record DiscoveryCandidate(
    ArtistKey Artist,
    string? ImageUrl,
    double Score,
    IReadOnlyList<string> Sources,
    int Depth);

/// <summary>A page of pending candidates plus the total pending count (for paging controls).</summary>
public record DiscoveryPage(
    IReadOnlyList<DiscoveryCandidate> Items,
    int Page,
    int PageSize,
    long TotalPending);

/// <summary>
/// The category a discovery-feed item belongs to. The feed is split into these so the UI can show
/// each as its own checkbox-toggleable, independently-paged section.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeedKind
{
    /// <summary>A new artist not in the library, grown from the user's liked artists.</summary>
    RecommendedArtist,

    /// <summary>An album on Deezer for an owned artist that isn't in the library yet.</summary>
    MissingAlbum,

    /// <summary>
    /// An owned library artist the user hasn't thumbed yet (either section, used for the Ratings
    /// classification and the legacy single-kind endpoint).
    /// </summary>
    LibraryArtist,

    /// <summary>
    /// An owned, unrated artist that a <em>liked</em> artist recommends — worth rating because the
    /// frontier already vouches for it. Carries the liked artists that point at it as provenance.
    /// </summary>
    RecommendedLibraryArtist,

    /// <summary>
    /// An owned, unrated artist nothing in the frontier recommends yet — rating it seeds the graph
    /// (a fresh taste anchor to grow recommendations from).
    /// </summary>
    SeedLibraryArtist,
}

/// <summary>
/// One thing to react to in the discovery feed. <paramref name="Album"/> and
/// <paramref name="DeezerAlbumId"/> are set only for <see cref="FeedKind.MissingAlbum"/> (the id lets
/// the UI sample/link the album on Deezer); <paramref name="Score"/>/<paramref name="Sources"/> rank
/// and explain recommended artists (0/empty for the other kinds).
/// </summary>
public record FeedItem(
    FeedKind Kind,
    ArtistKey Artist,
    string? Album,
    string? ImageUrl,
    double Score,
    IReadOnlyList<string> Sources,
    long? DeezerAlbumId);

/// <summary>A paged feed section for a single <see cref="FeedKind"/>.</summary>
public record DiscoveryFeedPage(
    FeedKind Kind,
    IReadOnlyList<FeedItem> Items,
    int Page,
    int PageSize,
    long Total);

/// <summary>
/// A rating the user has made, for the Ratings review page. <paramref name="Album"/> is set for
/// album ratings; <paramref name="Kind"/> is the effective category (an owned rated artist is
/// <see cref="FeedKind.LibraryArtist"/>, a non-owned one <see cref="FeedKind.RecommendedArtist"/>).
/// </summary>
public record RatedItem(
    FeedKind Kind,
    ArtistKey Artist,
    string? Album,
    string? ImageUrl,
    DiscoveryStatus Verdict,
    DateTimeOffset? SnoozeUntil = null);

/// <summary>
/// An artist-level rating row (verdict + image) read back from the per-user queue.
/// <paramref name="SnoozeUntil"/> is set only for <see cref="DiscoveryStatus.Snoozed"/> rows.
/// </summary>
public record ArtistRating(ArtistKey Artist, string? ImageUrl, DiscoveryStatus Status, DateTimeOffset? SnoozeUntil = null);

/// <summary>
/// One album in an artist's full discography, for the Artists-page drill-down. <paramref name="Owned"/>
/// marks albums already in the library; missing ones carry <paramref name="DeezerAlbumId"/> so they can
/// be queued to buy. <paramref name="Verdict"/> reflects any rating the user has placed on a missing
/// album (null = not yet decided, or owned). Owned albums the library has that Deezer doesn't list as an
/// LP carry no Deezer id/art.
/// </summary>
public record ArtistAlbumItem(
    ArtistKey Artist,
    string Album,
    string? ImageUrl,
    long? DeezerAlbumId,
    bool Owned,
    DiscoveryStatus? Verdict);
