using Autofac;
using MusicCrawler.Fakes.Services.Singletons;
using Noggog.Autofac;

namespace MusicCrawler.Fakes;

public class FakesModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(FakeLibraryQuery).Assembly)
            .InNamespacesOf(
                typeof(FakeLibraryQuery))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}