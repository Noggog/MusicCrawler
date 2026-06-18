namespace MusicCrawler.Backend.Services.Download;

/// <summary>
/// Configuration for the Deezer download subsystem, read from environment variables in MainModule
/// (no hardcoded config). <see cref="Automatic"/> controls only the background drainer — manual
/// "download now" works regardless. The Deezer <c>ARL</c> itself lives in <b>streamrip's own config</b>
/// (set once with <c>rip config</c> on the server), not here, so the credential never enters this
/// app's surface. We own the orchestration: what to grab, how fast, and where it lands.
/// </summary>
public record DownloaderConfig(
    bool Automatic,
    string DownloadDir,
    string RipBinary,
    string Quality,
    string FallbackQuality,
    string Codec,
    int BatchSize,
    TimeSpan ItemDelay,
    TimeSpan BatchInterval,
    TimeSpan DownloadTimeout);
