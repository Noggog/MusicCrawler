using Autofac;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;
using MusicCrawler.Spotify;

namespace MusicCrawler.Tests;

public static class FakeBaseIocContainer
{
    public static ContainerBuilder fakeBaseIocContainer()
    {
        ContainerBuilder builder = new ContainerBuilder();
        builder.RegisterModule<LibModule>();
        builder.RegisterModule<SpotifyDataModule>();
        return builder;
    }
}