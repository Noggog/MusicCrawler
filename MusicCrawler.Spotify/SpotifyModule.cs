using Autofac;
using MusicCrawler.Spotify.Inputs;

namespace MusicCrawler.Spotify;

public class SpotifyModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<SpotifyEnvironmentModule>();
        builder.RegisterModule<SpotifyDataModule>();
        builder.RegisterInstance(
                new SpotifyEndpointInfo(
                    BaseUri: "https://api.spotify.com",
                    RedirectUri: "http://localhost/"))
            .AsSelf().SingleInstance();
    }
}