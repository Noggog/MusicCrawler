using Autofac;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;
using MusicCrawler.MongoDB;
using MusicCrawler.Spotify;

namespace MusicCrawler.Tests.IntegrationTests;

public static class FakeBaseIocContainer
{
    public static ContainerBuilder fakeBaseIocContainer()
    {
        ContainerBuilder builder = new ContainerBuilder();
        builder.RegisterModule<LibModule>();
        builder.RegisterModule<SpotifyDataModule>();
        builder.RegisterModule<MongoDbDataModule>();
        builder
            .RegisterInstance(new FakeMongoDbProvider())
            .AsImplementedInterfaces()
            .SingleInstance();
        return builder;
    }
}