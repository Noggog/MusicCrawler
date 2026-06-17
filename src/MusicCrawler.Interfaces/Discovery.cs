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
