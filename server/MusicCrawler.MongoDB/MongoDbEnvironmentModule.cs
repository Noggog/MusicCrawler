using Autofac;
using MusicCrawler.MongoDB.Services.Singletons;
using Noggog.Autofac;

namespace MusicCrawler.MongoDB;

public class MongoDbEnvironmentModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(MongoDbProvider).Assembly)
            .InNamespacesOf(
                typeof(MongoDbProvider))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}