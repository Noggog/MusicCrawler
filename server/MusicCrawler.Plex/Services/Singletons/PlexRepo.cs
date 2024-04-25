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
        var preferredPlexLibrary = Environment.GetEnvironmentVariable("preferredPlexLibrary");
        PlexLibrary? plexLibrary = null;
        if (preferredPlexLibrary == null)
        {
            Console.WriteLine("Warning. Could not find preferred library. Falling back to random artist library because preferredPlexLibrary was null");
        }
        else if (plexLibraries.FirstOrDefault(it => string.Equals(it.Title, preferredPlexLibrary)) == null)
        {
            Console.WriteLine("Warning. Could not find preferred library. Falling back to random artist library because plexLibraries.FirstOrDefault was null");
        }
        else
        {
            plexLibrary = plexLibraries.First(it => string.Equals(it.Title, preferredPlexLibrary));
            Console.WriteLine("Successfully found preferred plexLibrary:" + plexLibrary.Title);
        }

        if (plexLibrary == null)
        {
            plexLibrary = plexLibraries.Where(it => it.Type == "artist").TakeRandomly(1).First();
            Console.WriteLine("Fallback used.");
        }

        return (await _plexApi.GetMusicArtists(plexLibrary.Key))
            .Select(plexMusicArtist =>
                    new ArtistMetadata(
                        Key: new ArtistKey(plexMusicArtist.Title),
                        ArtistImageUrl: null) // TODO: Shouldn't just be null
            )
            .ToArray();
    }
}