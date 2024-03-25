using Autofac;
using MusicCrawler.MongoDB.Services.Singletons;
using Noggog.Autofac;

namespace MusicCrawler.MongoDB;

public class MongoDbModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(MongoDbWrapper).Assembly)
            .InNamespacesOf(
                typeof(MongoDbWrapper))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}