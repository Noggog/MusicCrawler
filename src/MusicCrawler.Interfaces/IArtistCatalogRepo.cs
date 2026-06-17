namespace MusicCrawler.Interfaces;

/// <summary>
/// Local store of the artists known to exist in the (shared) Plex library.
/// This is the source of truth for daily reads — the Plex server is only touched
/// by the sync job that calls <see cref="SyncFromLibrary"/>.
/// </summary>
public interface IArtistCatalogRepo
{
    /// <summary>Artists currently present in the library, ordered by name.</summary>
    Task<CatalogArtist[]> GetAllPresent();

    /// <summary>
    /// Upserts the catalog from a Plex pull: every supplied artist is marked present
    /// with <paramref name="syncedAt"/>; any artist not seen in this sync is marked absent
    /// (kept, not deleted, so taste state can still reference it).
    /// </summary>
    Task<CatalogSyncResult> SyncFromLibrary(IReadOnlyList<ArtistMetadata> artists, DateTimeOffset syncedAt);

    /// <summary>
    /// Fills in <c>ArtistImageUrl</c> for artists already in the catalog (e.g. from a Deezer
    /// ingestion pass, since the Plex sync supplies no images). Only artists that already exist
    /// are touched — this never creates phantom catalog entries for artists outside the library,
    /// and only sets the image when one is supplied. Returns the number of docs updated.
    /// </summary>
    Task<int> BackfillImages(IReadOnlyList<ArtistMetadata> artists);

    /// <summary>
    /// Stores the owned album titles for each artist (from the same Plex pull as the artist list),
    /// so the missing-album diff can run against the local catalog. Only touches artists already
    /// present — never creates phantom entries.
    /// </summary>
    Task SyncAlbums(IReadOnlyList<ArtistAlbums> artistAlbums);

    /// <summary>
    /// The owned album titles per artist, keyed by artist name (case-insensitive). Used by the
    /// missing-album diff and to hide ratings for albums that have since been acquired.
    /// </summary>
    Task<Dictionary<string, HashSet<string>>> GetOwnedAlbums();

    /// <summary>
    /// Names of present catalog artists that encode multiple artists joined by ';' (a Plex
    /// multi-value artifact, e.g. "Nina Simone;Hot Chip") — candidates for cleanup.
    /// </summary>
    Task<string[]> FindCombinedArtistNames();

    /// <summary>
    /// Splits a ';'-joined catalog artist into one present doc per <paramref name="parts"/> name
    /// (each inheriting the combined doc's albums), then deletes the combined doc. Idempotent:
    /// parts that already exist keep their data and just absorb any albums.
    /// </summary>
    Task SplitCombinedArtist(string combinedName, IReadOnlyList<string> parts, DateTimeOffset syncedAt);
}

public record CatalogSyncResult(int Upserted, int MarkedAbsent, int TotalPresent);
