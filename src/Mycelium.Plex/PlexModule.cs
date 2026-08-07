using Autofac;
using Mycelium.Plex.Services.Singletons;
using Noggog.Autofac;

namespace Mycelium.Plex;

public class PlexModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(PlexApi).Assembly)
            .InNamespacesOf(
                typeof(PlexApi))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}