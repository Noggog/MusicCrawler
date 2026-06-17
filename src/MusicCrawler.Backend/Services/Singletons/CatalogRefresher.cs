using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// The Library Catalog sync job: pulls artists from Plex and upserts them into the
/// local catalog store. This is the only path that touches Plex — daily reads go
/// through <see cref="ILibraryProvider"/> against the stored catalog instead.
/// </summary>
public class CatalogRefresher
{
    private readonly ILibraryQuery _libraryQuery;
    private readonly IArtistCatalogRepo _catalog;
    private readonly ILogger<CatalogRefresher> _logger;

    public CatalogRefresher(
        ILibraryQuery libraryQuery,
        IArtistCatalogRepo catalog,
        ILogger<CatalogRefresher> logger)
    {
        _libraryQuery = libraryQuery;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<CatalogSyncResult> Refresh()
    {
        var artists = await _libraryQuery.QueryAllArtistMetadata();
        var syncedAt = DateTimeOffset.UtcNow;
        var result = await _catalog.SyncFromLibrary(artists, syncedAt);
        _logger.LogInformation(
            "Catalog refresh: {Upserted} upserted, {MarkedAbsent} marked absent, {TotalPresent} present",
            result.Upserted, result.MarkedAbsent, result.TotalPresent);
        return result;
    }
}
