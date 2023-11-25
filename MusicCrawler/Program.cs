using MusicCrawler.Lib;

PlexApi plex = new PlexApi(args[0], args[1]);

try
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
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}