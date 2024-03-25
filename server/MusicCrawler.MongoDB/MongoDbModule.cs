using Autofac;

namespace MusicCrawler.MongoDB;

public class MongoDbModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<MongoDbDataModule>();
        builder.RegisterModule<MongoDbEnvironmentModule>();
    }
}