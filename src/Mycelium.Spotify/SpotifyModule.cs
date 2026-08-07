using Autofac;
using Mycelium.Spotify.Inputs;

namespace Mycelium.Spotify;

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