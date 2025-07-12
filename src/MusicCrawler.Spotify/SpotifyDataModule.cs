using Autofac;
using MusicCrawler.Spotify.Services.Data;
using Noggog.Autofac;

namespace MusicCrawler.Spotify;

public class SpotifyDataModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(SpotifyProvider).Assembly)
            .InNamespacesOf(
                typeof(SpotifyProvider))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}