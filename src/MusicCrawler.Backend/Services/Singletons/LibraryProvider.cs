using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

public interface ILibraryProvider
{
    Task<ArtistMetadata[]> GetAllArtistMetadata();
}

/// <summary>
/// Serves the artist library for daily reads from the local catalog store — not from
/// Plex. Keeping reads off the live Plex path is what lets the app stay usable when
/// Plex is offline; the catalog is refreshed separately by <see cref="CatalogRefresher"/>.
/// </summary>
public class LibraryProvider : ILibraryProvider
{
    private readonly IArtistCatalogRepo _catalog;

    public LibraryProvider(IArtistCatalogRepo catalog)
    {
        _catalog = catalog;
    }

    public async Task<ArtistMetadata[]> GetAllArtistMetadata()
    {
        return (await _catalog.GetAllPresent())
            .Select(x => new ArtistMetadata(x.ArtistKey, x.ArtistImageUrl))
            .ToArray();
    }
}
