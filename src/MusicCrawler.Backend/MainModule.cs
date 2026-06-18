using Autofac;
using Microsoft.Extensions.Hosting;
using MusicCrawler.Backend.Services.Background;
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

        // Periodic queue replenisher cadence (env knob, default 24h). First run is offset ~5min past
        // boot so it lands after the catalog + album syncs rather than contending with them on Deezer.
        var replenishHours = double.TryParse(
            Environment.GetEnvironmentVariable("QUEUE_REPLENISH_INTERVAL_HOURS"), out var h) ? h : 24;
        builder.RegisterInstance(new ReplenishConfig(
            Interval: TimeSpan.FromHours(replenishHours), StartupDelay: TimeSpan.FromMinutes(5)));

        builder.RegisterInstance(
            new PlexEndpointInfo(Environment.GetEnvironmentVariable("PLEX_ENDPOINT") ?? throw new InvalidOperationException()));
        builder.RegisterInstance(
            new PlexClientInfo(Environment.GetEnvironmentVariable("PLEX_TOKEN") ?? throw new InvalidOperationException()));
        builder.RegisterType<HttpClient>().AsSelf().SingleInstance();

        // Deezer download subsystem (env-driven; ARL lives in streamrip's own config). streamrip is
        // always the backend; DEEZER_DOWNLOADS_AUTOMATIC only governs the background drainer — manual
        // "download now" works regardless. DownloadService is a shared singleton hosted service so the
        // endpoint that enqueues a manual download and the loop that drains it are the same instance.
        builder.RegisterInstance(BuildDownloaderConfig());
        builder.RegisterType<StreamripDownloader>().As<IDownloader>().AsSelf().SingleInstance();
        builder.RegisterType<DownloadService>().AsSelf().As<IHostedService>().SingleInstance();

        // Post-download Plex rescan (PlexLibraryScanner auto-registers as ILibraryScanner via the
        // assembly scan below). Off unless PLEX_RESCAN_AFTER_DOWNLOAD is set; debounce defaults to 5m.
        builder.RegisterInstance(BuildLibraryScannerConfig());

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
            Automatic: EnvBool("DEEZER_DOWNLOADS_AUTOMATIC"),
            DownloadDir: Env("MUSIC_DOWNLOAD_DIR"),
            RipBinary: Environment.GetEnvironmentVariable("STREAMRIP_BIN") ?? "rip",
            Quality: Environment.GetEnvironmentVariable("DEEZER_QUALITY") ?? "2",          // streamrip: 2 = FLAC
            FallbackQuality: Environment.GetEnvironmentVariable("DEEZER_FALLBACK_QUALITY") ?? "1", // 1 = 320kbps MP3
            Codec: Env("DEEZER_CODEC"), // empty = streamrip default (keep source codec)
            BatchSize: EnvInt("DOWNLOAD_BATCH_SIZE", 3),
            ItemDelay: TimeSpan.FromSeconds(EnvDouble("DOWNLOAD_ITEM_DELAY_SECONDS", 60)),
            BatchInterval: TimeSpan.FromMinutes(EnvDouble("DOWNLOAD_BATCH_INTERVAL_MINUTES", 30)),
            DownloadTimeout: TimeSpan.FromMinutes(EnvDouble("DEEZER_DOWNLOAD_TIMEOUT_MINUTES", 15)));
    }

    private static LibraryScannerConfig BuildLibraryScannerConfig()
    {
        var enabled = Environment.GetEnvironmentVariable("PLEX_RESCAN_AFTER_DOWNLOAD") is var v
                      && (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
        var debounceMinutes = double.TryParse(
            Environment.GetEnvironmentVariable("PLEX_RESCAN_DEBOUNCE_MINUTES"), out var m) ? m : 5;
        return new LibraryScannerConfig(enabled, TimeSpan.FromMinutes(debounceMinutes));
    }
}