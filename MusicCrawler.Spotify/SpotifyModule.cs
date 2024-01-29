using Autofac;
using MusicCrawler.Spotify.Services.Singletons;
using Noggog.Autofac;

namespace MusicCrawler.Spotify;

public class SpotifyModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(SpotifyApi).Assembly)
            .InNamespacesOf(
                typeof(SpotifyApi))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
        builder.RegisterInstance(
            new SpotifyEndpointInfo(
                BaseUri: "https://api.spotify.com",
                RedirectUri: "http://localhost/"))
            .AsSelf().SingleInstance();
    }
}