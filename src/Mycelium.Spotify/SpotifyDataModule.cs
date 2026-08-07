using Autofac;
using Mycelium.Spotify.Services;
using Noggog.Autofac;

namespace Mycelium.Spotify;

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