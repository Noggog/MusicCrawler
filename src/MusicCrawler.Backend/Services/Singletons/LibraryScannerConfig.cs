// Lives in the MusicCrawler.Backend root namespace (NOT Services.Singletons) on purpose: MainModule's
// assembly scan sweeps Services.Singletons AsSelf via reflection, which would shadow the env-built
// RegisterInstance below with a non-constructable reflection registration (no parameterless ctor) and
// fail activation. Config records belong outside the scanned namespace — same as RelatedStalenessPolicy.
namespace MusicCrawler.Backend;

/// <summary>
/// Configuration for the post-download Plex rescan, read from environment variables in MainModule
/// (no hardcoded config). <see cref="Enabled"/> (<c>PLEX_RESCAN_AFTER_DOWNLOAD</c>) is a server-wide
/// opt-in, off by default; <see cref="Debounce"/> (<c>PLEX_RESCAN_DEBOUNCE_MINUTES</c>) is how long
/// downloads must quiet down before a single coalesced scan fires.
/// </summary>
public record LibraryScannerConfig(bool Enabled, TimeSpan Debounce);
