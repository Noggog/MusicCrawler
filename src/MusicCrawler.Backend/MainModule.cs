using Autofac;
using MusicCrawler.Backend.Services.Download;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Deezer;
using MusicCrawler.Interfaces;
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

        // Deezer download subsystem (env-driven; ARL lives in streamrip's own config). Disabled
        // unless DEEZER_DOWNLOADS_ENABLED is truthy — then streamrip is the backend, else a no-op.
        var downloaderConfig = BuildDownloaderConfig();
        builder.RegisterInstance(downloaderConfig);
        if (downloaderConfig.Enabled)
        {
            builder.RegisterType<StreamripDownloader>().As<IDownloader>().AsSelf().SingleInstance();
        }
        else
        {
            builder.RegisterType<NoOpDownloader>().As<IDownloader>().AsSelf().SingleInstance();
        }

        builder.RegisterAssemblyTypes(typeof(LibraryProvider).Assembly)
            .InNamespacesOf(
                typeof(LibraryProvider))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }

    private static DownloaderConfig BuildDownloaderConfig()
    {
        static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";
        static bool EnvBool(string name) =>
            Env(name) is var v && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
        static double EnvDouble(string name, double fallback) =>
            double.TryParse(Environment.GetEnvironmentVariable(name), out var d) ? d : fallback;
        static int EnvInt(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var i) ? i : fallback;

        return new DownloaderConfig(
            Enabled: EnvBool("DEEZER_DOWNLOADS_ENABLED"),
            DownloadDir: Env("MUSIC_DOWNLOAD_DIR"),
            RipBinary: Environment.GetEnvironmentVariable("STREAMRIP_BIN") ?? "rip",
            Quality: Environment.GetEnvironmentVariable("DEEZER_QUALITY") ?? "1", // streamrip: 1 = 320kbps MP3
            Codec: Env("DEEZER_CODEC"), // empty = streamrip default
            BatchSize: EnvInt("DOWNLOAD_BATCH_SIZE", 3),
            ItemDelay: TimeSpan.FromSeconds(EnvDouble("DOWNLOAD_ITEM_DELAY_SECONDS", 60)),
            BatchInterval: TimeSpan.FromMinutes(EnvDouble("DOWNLOAD_BATCH_INTERVAL_MINUTES", 30)));
    }
}