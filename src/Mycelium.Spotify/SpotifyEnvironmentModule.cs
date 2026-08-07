using Autofac;
using Mycelium.Spotify.Services;
using Noggog.Autofac;

namespace Mycelium.Spotify;

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