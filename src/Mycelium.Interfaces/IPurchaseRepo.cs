using System.Text.Json.Serialization;

namespace Mycelium.Interfaces;

/// <summary>Where a queued purchase sits in its acquisition lifecycle.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PurchaseStatus
{
    /// <summary>Queued to acquire — liked by at least one user, not yet ordered.</summary>
    Pending,

    /// <summary>Requested for download and waiting in the drainer's in-memory queue. Counts under
    /// "downloading" in the monitor (the drainer is single-flight, so most sit here); reset to
    /// Pending on restart, since the queue is in-memory and doesn't survive a crash.</summary>
    Queued,

    /// <summary>Actively being fetched by the downloader right now (single-flight).</summary>
    Downloading,

    /// <summary>Downloaded (or ordered by hand) — awaiting arrival in the library.</summary>
    Sent,

    /// <summary>Now present in the Plex library — the loop is closed; drops off the active list.</summary>
    InLibrary,

    /// <summary>The downloader could not acquire it (resolve/download error) — surfaced for retry.</summary>
    Failed,
}

/// <summary>
/// One item on the shared acquisition list: an artist to buy, or a missing album to fill in. The
/// list is global — the library maintainer's queue, unified across users — but persisted with a
/// <see cref="Status"/> (pending → sent → in-library) so ordering progress survives restarts and
/// isn't recomputed away. <see cref="Kind"/> is <see cref="FeedKind.RecommendedArtist"/> (no album)
/// or <see cref="FeedKind.MissingAlbum"/>.
/// </summary>
public record PurchaseItem(
    string Id,
    FeedKind Kind,
    ArtistKey Artist,
    string? Album,
    string? ImageUrl,
    double Score,
    IReadOnlyList<string> Sources,
    PurchaseStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SentAt,
    long? DeezerAlbumId,
    // The act the library files this album under (Deezer's album-artist) — used to match ownership
    // for a collaboration whose listing/display artist differs. Null for artists and non-collabs.
    string? AlbumArtist = null);

/// <summary>
/// A live snapshot of the download subsystem for the monitoring panel: whether downloads are on,
/// which backend, the throttle settings, current counts by lifecycle stage, and the item(s) being
/// fetched right now (normally one — the drainer is single-flight).
/// </summary>
public record DownloadSnapshot(
    bool Automatic,
    string Backend,
    int BatchSize,
    double ItemDelaySeconds,
    double BatchIntervalMinutes,
    // How far the two waits above are randomly spread, as a percentage — so the panel doesn't read as
    // a promise of exact timing when it isn't one.
    double JitterPercent,
    int Queued,
    int Downloading,
    int Complete,
    int Failed,
    PurchaseItem[] Current,
    // When the drainer next expects to act: NextItemAt is the end of the wait between two albums
    // (null unless it's mid-wait), NextBatchAt the next automatic sweep for pending albums. Both null
    // when nothing is scheduled — e.g. before the first pass has run.
    DateTimeOffset? NextItemAt = null,
    DateTimeOffset? NextBatchAt = null);

/// <summary>
/// Canonical id for a purchase row — "artist:{name}" or "album:{artist} {album}", lower-cased. One
/// definition shared by the store and the reconciler so an item is keyed identically everywhere.
/// </summary>
public static class PurchaseKey
{
    public static string ForArtist(string artist) => $"artist:{artist.ToLowerInvariant()}";

    public static string ForAlbum(string artist, string album) =>
        $"album:{artist.ToLowerInvariant()} {album.ToLowerInvariant()}";
}

/// <summary>
/// The shared acquisition store. One doc per item, global (not per-user). Reads serve the "to buy"
/// page; the reconciler folds in the current liked-but-unowned set and closes out arrivals.
/// </summary>
public interface IPurchaseRepo
{
    /// <summary>All purchase rows, newest request first.</summary>
    Task<PurchaseItem[]> GetAll();

    /// <summary>
    /// Inserts the item as <see cref="PurchaseStatus.Pending"/> if absent (stamping RequestedAt), or
    /// refreshes its display fields (image/score/sources) without touching status — so a reconcile
    /// re-run never demotes a Sent/InLibrary row back to Pending or resets its age.
    /// </summary>
    Task Upsert(PurchaseItem item);

    /// <summary>
    /// Sets a row's status (stamping SentAt when moving to <see cref="PurchaseStatus.Sent"/>).
    /// Returns false if the id is unknown.
    /// </summary>
    Task<bool> SetStatus(string id, PurchaseStatus status);

    /// <summary>Removes a row entirely (no longer wanted).</summary>
    Task Remove(string id);
}
