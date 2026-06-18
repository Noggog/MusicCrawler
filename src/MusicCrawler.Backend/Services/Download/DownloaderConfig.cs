namespace MusicCrawler.Backend.Services.Download;

/// <summary>
/// Configuration for the Deezer download subsystem, read from environment variables in MainModule
/// (no hardcoded config). When <see cref="Enabled"/> is false the subsystem is off — the
/// <see cref="NoOpDownloader"/> is registered and the background drainer idles.
///
/// The Deezer <c>ARL</c> itself lives in <b>streamrip's own config</b> (set once with <c>rip config</c>
/// on the server), not here — so the credential never enters this app's surface. We own the
/// orchestration: what to grab, how fast, and where it lands.
/// </summary>
public record DownloaderConfig(
    bool Enabled,
    string DownloadDir,
    string RipBinary,
    string Quality,
    string Codec,
    int BatchSize,
    TimeSpan ItemDelay,
    TimeSpan BatchInterval);
