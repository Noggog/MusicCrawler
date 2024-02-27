﻿using Autofac;
using MusicCrawler.Lib;
using MusicCrawler.Plex;
using MusicCrawler.Spotify;
using MusicCrawler.Spotify.Inputs;

namespace MusicCrawler.Backend;

// TODO: Should this be moved somewhere so that a CLI would also have access to it?
public class MainModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<LibModule>();
        builder.RegisterModule<PlexModule>();
        builder.RegisterModule<SpotifyModule>();
        builder.RegisterInstance(
            new SpotifyClientInfo(
                Id: "267c94026025449b8013ddde6d959e13",
                Secret: "92c88db9315545e38989ca8cc4cad2ad"));
        builder.RegisterInstance(
            new PlexEndpointInfo(Environment.GetEnvironmentVariable("plexEndpoint") ?? throw new InvalidOperationException()));
        builder.RegisterInstance(
            new PlexClientInfo(Environment.GetEnvironmentVariable("plexClientSecret") ?? throw new InvalidOperationException()));
        builder.RegisterType<HttpClient>().AsSelf().SingleInstance();
    }
}