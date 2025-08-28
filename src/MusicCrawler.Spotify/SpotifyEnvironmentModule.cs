using Autofac;
using MusicCrawler.Spotify.Services;
using Noggog.Autofac;

namespace MusicCrawler.Spotify;

public class SpotifyEnvironmentModule : Module
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