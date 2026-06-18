using Microsoft.Extensions.Logging;
using MusicCrawler.Interfaces;
using MusicCrawler.Plex.Services;

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
        var plexLibrary = await _plexApi.ResolveLibrary();
        // Plex joins collaborators into one title with ';' — split them so "Nina Simone;Hot Chip"
        // becomes two artists, then dedup (the split halves can collide with standalone entries).
        return (await _plexApi.GetMusicArtists(plexLibrary.Key))
            .SelectMany(plexMusicArtist => ArtistNames.Split(plexMusicArtist.Title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name =>
                    new ArtistMetadata(
                        ArtistKey: new ArtistKey(name),
                        ArtistImageUrl: null) // TODO: Shouldn't just be null
            )
            .ToArray();
    }

    public async Task<ArtistAlbums[]> QueryAllAlbums()
    {
        var plexLibrary = await _plexApi.ResolveLibrary();
        // Split a ';'-joined ParentTitle so a collaborative album is credited to each artist, then
        // regroup by the real artist name (matching the split done in QueryAllArtistMetadata).
        return (await _plexApi.GetMusicAlbums(plexLibrary.Key))
            .Where(a => !string.IsNullOrWhiteSpace(a.ParentTitle) && !string.IsNullOrWhiteSpace(a.Title))
            .SelectMany(a => ArtistNames.Split(a.ParentTitle).Select(name => (Name: name, a.Title)))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ArtistAlbums(
                new ArtistKey(g.Key),
                g.Select(x => x.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
    }
}