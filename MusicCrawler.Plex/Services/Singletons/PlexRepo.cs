using MusicCrawler.Lib;

namespace MusicCrawler.Plex.Services.Singletons;

public class PlexRepo : ILibraryQuery
{
    private readonly PlexApi _plexApi;

    public PlexRepo(PlexApi plexApi)
    {
        _plexApi = plexApi;
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
        var plexLibraries = await _plexApi.GetLibraries();
        var plexLibrary = plexLibraries.Where(it => it.Type == "artist").TakeRandomly(1).First(); // TODO: Probably should select a predefined library.
        return (await _plexApi.GetMusicArtists(plexLibrary.Key))
            .Select(plexMusicArtist =>
                    new ArtistMetadata(
                        Key: new ArtistKey(plexMusicArtist.Title),
                        ArtistImageUrl: null) // TODO: Shouldn't just be null
            )
            .ToArray();
    }
}