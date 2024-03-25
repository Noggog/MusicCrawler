using Autofac;
using MusicCrawler.Lib.Services.Singletons;
using Noggog.Autofac;

namespace MusicCrawler.Lib;

public class LibModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(RandomStringGenerator).Assembly)
            .InNamespacesOf(
                typeof(RandomStringGenerator))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<Random>().AsSelf().SingleInstance();
    }
}