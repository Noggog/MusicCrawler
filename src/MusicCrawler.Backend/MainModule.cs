using Autofac;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Deezer;
using MusicCrawler.MongoDB;
using MusicCrawler.Plex;
using Noggog.Autofac;

namespace MusicCrawler.Backend;

public class MainModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<PlexModule>();
        builder.RegisterModule<MongoDbModule>();
        // Deezer is the live recommendation source (DeezerProvider : IRecommendationProvider),
        // replacing the deprecated Spotify recommendations API.
        builder.RegisterModule<DeezerModule>();

        // How long a stored similarity-graph edge set stays fresh before re-ingestion (env knob,
        // default 30 days). Read once here so the ingestion service stays env-free and testable.
        var stalenessDays = double.TryParse(
            Environment.GetEnvironmentVariable("RELATED_STALENESS_DAYS"), out var d) ? d : 30;
        builder.RegisterInstance(new RelatedStalenessPolicy(TimeSpan.FromDays(stalenessDays)));
        builder.RegisterInstance(
            new PlexEndpointInfo(Environment.GetEnvironmentVariable("PLEX_ENDPOINT") ?? throw new InvalidOperationException()));
        builder.RegisterInstance(
            new PlexClientInfo(Environment.GetEnvironmentVariable("PLEX_TOKEN") ?? throw new InvalidOperationException()));
        builder.RegisterType<HttpClient>().AsSelf().SingleInstance();
        
        builder.RegisterAssemblyTypes(typeof(LibraryProvider).Assembly)
            .InNamespacesOf(
                typeof(LibraryProvider))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}