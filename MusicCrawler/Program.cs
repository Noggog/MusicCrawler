﻿using Autofac;
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
builder.RegisterModule<SpotifyModule>();
builder.RegisterModule<PlexModule>();
builder.RegisterModule<FakesModule>();
builder.RegisterInstance(
    new SpotifyClientInfo(
        Id: "267c94026025449b8013ddde6d959e13",
        Secret: "92c88db9315545e38989ca8cc4cad2ad"));
builder.RegisterInstance(
    new PlexEndpointInfo(args[0]));
builder.RegisterInstance(
    new PlexClientInfo(args[1]));
builder.RegisterType<HttpClient>().AsSelf().SingleInstance();
var container = builder.Build();

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

async Task ManuallyVerifySpotifyApi()
{
    SpotifyRepo spotifyRepo = container.Resolve<SpotifyRepo>();
    var result = await spotifyRepo.Recommendations("4NHQUGzhtTLFvgF5SZesLK");
    Console.WriteLine($"result: {result}");
}

async Task ManuallyVerifyRecommendationInteractor()
{
    RecommendationInteractor recommendationInteractor = container.Resolve<RecommendationInteractor>();
    var result = await recommendationInteractor.Recommendations();
    Console.WriteLine($"result: {result.ToLogStr()}");
}

// try
// {
    await ManuallyVerifyRecommendationInteractor();
    // await PrintLibrariesAndRecentlyAdded();

    // var artists = await plex.GetMusicArtists(1);
    // foreach (var artist in artists)
    // {
    //     Console.WriteLine($"Library: {artist})");
    // }
// }
// catch (Exception ex)
// {
//     Console.WriteLine($"Error: {ex}");
// }