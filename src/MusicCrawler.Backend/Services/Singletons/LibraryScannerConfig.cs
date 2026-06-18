namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Configuration for the post-download Plex rescan, read from environment variables in MainModule
/// (no hardcoded config). <see cref="Enabled"/> (<c>PLEX_RESCAN_AFTER_DOWNLOAD</c>) is a server-wide
/// opt-in, off by default; <see cref="Debounce"/> (<c>PLEX_RESCAN_DEBOUNCE_MINUTES</c>) is how long
/// downloads must quiet down before a single coalesced scan fires.
/// </summary>
public record LibraryScannerConfig(bool Enabled, TimeSpan Debounce);
