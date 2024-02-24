using Autofac;
using MusicCrawler.Spotify.Inputs;
using MusicCrawler.Spotify.Services.Environment;
using MusicCrawler.Spotify.Services.Singletons;
using Noggog.Autofac;

namespace MusicCrawler.Spotify;

public class SpotifyEnvironmentModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(SpotifyApi).Assembly)
            .InNamespacesOf(
                typeof(SpotifyApi))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}