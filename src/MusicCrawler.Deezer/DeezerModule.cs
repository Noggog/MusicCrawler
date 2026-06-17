using Autofac;
using MusicCrawler.Deezer.Inputs;
using MusicCrawler.Deezer.Services;
using Noggog.Autofac;

namespace MusicCrawler.Deezer;

public class DeezerModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Deezer is keyless; the only setting is the base URI. Default to the public endpoint,
        // overridable via env var (no hardcoded config that can't be redirected in another env).
        builder.RegisterInstance(
                new DeezerEndpointInfo(
                    Environment.GetEnvironmentVariable("DEEZER_BASE_URI") ?? "https://api.deezer.com"))
            .AsSelf().SingleInstance();

        // Registers DeezerApi (IDeezerApi) and DeezerProvider (IRecommendationProvider), both in
        // the Services namespace, following the same assembly-scan convention as the other modules.
        builder.RegisterAssemblyTypes(typeof(DeezerApi).Assembly)
            .InNamespacesOf(typeof(DeezerApi))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}
