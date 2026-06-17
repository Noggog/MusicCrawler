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

    public CatalogRefresher(ILibraryQuery libraryQuery, IArtistCatalogRepo catalog)
    {
        _libraryQuery = libraryQuery;
        _catalog = catalog;
    }

    public async Task<CatalogSyncResult> Refresh()
    {
        var artists = await _libraryQuery.QueryAllArtistMetadata();
        var syncedAt = DateTimeOffset.UtcNow;
        var result = await _catalog.SyncFromLibrary(artists, syncedAt);
        Console.WriteLine(
            $"Catalog refresh: {result.Upserted} upserted, {result.MarkedAbsent} marked absent, {result.TotalPresent} present");
        return result;
    }
}
