using Autofac;
using Mycelium.MongoDB.Services.Data;
using Noggog.Autofac;

namespace Mycelium.MongoDB;

public class MongoDbDataModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(RecommendationPersistenceRepo).Assembly)
            .InNamespacesOf(
                typeof(RecommendationPersistenceRepo))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}