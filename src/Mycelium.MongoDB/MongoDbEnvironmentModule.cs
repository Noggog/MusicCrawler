using Autofac;
using Mycelium.MongoDB.Services.Environment;
using Noggog.Autofac;

namespace Mycelium.MongoDB;

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