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
        PlexLibrary plexLibrary;
        try
        {
            plexLibrary =
                plexLibraries
                    .First(it => string.Equals(it.Title, Environment.GetEnvironmentVariable("preferredPlexLibrary") ?? throw new InvalidOperationException()))
                    .Let(it => it ?? throw new Exception("Could not find preferredPlexLibrary in plexLibraries:" + plexLibraries.JoinToStr()))
                    .Also(it => Console.WriteLine("Successfully found preferred plexLibrary:" + it.Title));
        }
        catch (Exception e)
        {
            Console.WriteLine("Warning. Could not find preferred library. Falling back to random artist library because:\n" + e);
            plexLibrary = plexLibraries.Where(it => it.Type == "artist").TakeRandomly(1).First();
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