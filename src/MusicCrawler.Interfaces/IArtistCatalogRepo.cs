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
}

public record CatalogSyncResult(int Upserted, int MarkedAbsent, int TotalPresent);
