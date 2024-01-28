using MusicCrawler.Lib;
using MusicCrawler.Lib.Data;
using MusicCrawler.Lib.Spotify;

PlexApi plex = new PlexApi(args[0], args[1]);
SpotifyApi spotifyApi = new SpotifyApi();
SpotifyRepo spotifyRepo = new SpotifyRepo();

async Task PrintLibrariesAndRecentlyAdded()
{
    var libraries = await plex.GetLibraries();
    foreach (var library in libraries)
    {
        Console.WriteLine($"Library: {library.Title} (Key: {library.Key})");

        var recentlyAdded = await plex.GetRecentlyAdded(library.Key);
        foreach (var item in recentlyAdded)
        {
            Console.WriteLine($"  - {item.Title}");
        }
    }
}

async Task ManuallyVerifySpotifyApi()
{
    var result = await spotifyRepo.Recommendations("4NHQUGzhtTLFvgF5SZesLK");
    Console.WriteLine($"result: {result}");
}

try
{
    await ManuallyVerifySpotifyApi();
    // await PrintLibrariesAndRecentlyAdded();

    // var artists = await plex.GetMusicArtists(1);
    // foreach (var artist in artists)
    // {
    //     Console.WriteLine($"Library: {artist})");
    // }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}