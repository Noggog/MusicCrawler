using Autofac;
using MusicCrawler.Spotify.Services.Data;
using Noggog.Autofac;

namespace MusicCrawler.Spotify;

public class SpotifyDataModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(SpotifyRepo).Assembly)
            .InNamespacesOf(
                typeof(SpotifyRepo))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}