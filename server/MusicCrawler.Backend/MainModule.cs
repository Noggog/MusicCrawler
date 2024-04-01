using Autofac;
using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;
using MusicCrawler.MongoDB;
using MusicCrawler.Plex;
using MusicCrawler.Spotify;
using MusicCrawler.Spotify.Inputs;

namespace MusicCrawler.Backend;

// TODO: Should this be moved somewhere so that a CLI would also have access to it?
public class MainModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<LibModule>();
        builder.RegisterModule<PlexModule>();
        builder.RegisterModule<MongoDbModule>();
        builder.RegisterModule<SpotifyModule>();
        builder.RegisterInstance(
            new SpotifyClientInfo(
                Id: "267c94026025449b8013ddde6d959e13",
                Secret: "92c88db9315545e38989ca8cc4cad2ad"));
        builder.Register(c => new PlexEndpointInfo(c.Resolve<EnvironmentVariableProvider>().PlexEndpoint() ?? throw new InvalidOperationException()))
            .As<PlexEndpointInfo>();
        builder.Register(c => new PlexClientInfo(c.Resolve<EnvironmentVariableProvider>().PlexClientSecret() ?? throw new InvalidOperationException()))
            .As<PlexClientInfo>();
        builder.RegisterType<HttpClient>().AsSelf().SingleInstance();
    }
}