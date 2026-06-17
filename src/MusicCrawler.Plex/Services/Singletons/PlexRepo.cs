using Microsoft.Extensions.Logging;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Plex.Services.Singletons;

public class PlexRepo : ILibraryQuery
{
    private readonly PlexApi _plexApi;
    private readonly ILogger<PlexRepo> _logger;

    public PlexRepo(PlexApi plexApi, ILogger<PlexRepo> logger)
    {
        _plexApi = plexApi;
        _logger = logger;
    }

    public Task<ArtistPackage> QueryArtistPackage(ArtistKey artistKey)
    {
        throw new NotImplementedException();
    }

    public Task<ArtistPackage[]> QueryAllData()
    {
        throw new NotImplementedException();
    }

    public async Task<ArtistMetadata[]> QueryAllArtistMetadata()
    {
        var plexLibrary = await ResolveLibrary();
        return (await _plexApi.GetMusicArtists(plexLibrary.Key))
            .Select(plexMusicArtist =>
                    new ArtistMetadata(
                        ArtistKey: new ArtistKey(plexMusicArtist.Title),
                        ArtistImageUrl: null) // TODO: Shouldn't just be null
            )
            .ToArray();
    }

    public async Task<ArtistAlbums[]> QueryAllAlbums()
    {
        var plexLibrary = await ResolveLibrary();
        return (await _plexApi.GetMusicAlbums(plexLibrary.Key))
            .Where(a => !string.IsNullOrWhiteSpace(a.ParentTitle) && !string.IsNullOrWhiteSpace(a.Title))
            .GroupBy(a => a.ParentTitle, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ArtistAlbums(
                new ArtistKey(g.Key),
                g.Select(a => a.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Resolves the music library to read from: the one named by <c>PLEX_LIBRARY</c> when it
    /// matches, otherwise the first artist-type library. Logs which path it took.
    /// </summary>
    private async Task<PlexLibrary> ResolveLibrary()
    {
        var plexLibraries = await _plexApi.GetLibraries();
        var preferredPlexLibrary = Environment.GetEnvironmentVariable("PLEX_LIBRARY");
        PlexLibrary? plexLibrary = null;
        if (preferredPlexLibrary == null)
        {
            _logger.LogWarning("PLEX_LIBRARY not set; falling back to the first artist-type library.");
        }
        else if (plexLibraries.FirstOrDefault(it => string.Equals(it.Title, preferredPlexLibrary)) == null)
        {
            _logger.LogWarning(
                "Preferred Plex library {Library} not found; falling back to the first artist-type library.",
                preferredPlexLibrary);
        }
        else
        {
            plexLibrary = plexLibraries.First(it => string.Equals(it.Title, preferredPlexLibrary));
            _logger.LogInformation("Using preferred Plex library {Library}.", plexLibrary.Title);
        }

        if (plexLibrary == null)
        {
            plexLibrary = plexLibraries.Where(it => it.Type == "artist").Take(1).First();
            _logger.LogWarning("Fell back to artist-type Plex library {Library}.", plexLibrary.Title);
        }

        return plexLibrary;
    }
}