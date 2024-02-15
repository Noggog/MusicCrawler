using Autofac;
using MusicCrawler.Fakes;
using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;
using MusicCrawler.Plex;
using MusicCrawler.Plex.Services.Singletons;
using MusicCrawler.Spotify;
using MusicCrawler.Spotify.Inputs;
using MusicCrawler.Spotify.Services.Singletons;

var builder = new ContainerBuilder();
builder.RegisterModule<LibModule>();
builder.RegisterModule<PlexModule>();
builder.RegisterInstance(
    new SpotifyClientInfo(
        Id: "267c94026025449b8013ddde6d959e13",
        Secret: "92c88db9315545e38989ca8cc4cad2ad"));
builder.RegisterInstance(
    new PlexEndpointInfo(args[0]));
builder.RegisterInstance(
    new PlexClientInfo(args[1]));
builder.RegisterType<HttpClient>().AsSelf().SingleInstance();
IContainer container;
if (args.Length > 2 && args[2] == "ManuallyVerifyRecommendationInteractor")
{
    builder.RegisterModule<FakesModule>();
    container = builder.Build();
    
    RecommendationInteractor recommendationInteractor = container.Resolve<RecommendationInteractor>();
    var result = await recommendationInteractor.Recommendations();
    Console.WriteLine($"result: {result.ToLogStr()}");
}
else if (args.Length > 2 && args[2] == "ManuallyVerifySpotifyApi")
{
    builder.RegisterModule<SpotifyModule>();
    container = builder.Build();
    
    SpotifyRepo spotifyRepo = container.Resolve<SpotifyRepo>();
    var result = await spotifyRepo.Recommendations("4NHQUGzhtTLFvgF5SZesLK");
    Console.WriteLine($"result: {result}");
}
else
{
    builder.RegisterModule<SpotifyModule>();
    container = builder.Build();
    
    await PrintLibrariesAndRecentlyAdded();
}


async Task PrintLibrariesAndRecentlyAdded()
{
    var plex = container.Resolve<PlexApi>();
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