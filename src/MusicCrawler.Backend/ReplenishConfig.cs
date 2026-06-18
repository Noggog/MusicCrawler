namespace MusicCrawler.Backend;

/// <summary>
/// Cadence for the periodic queue replenisher. <paramref name="Interval"/> from the
/// QUEUE_REPLENISH_INTERVAL_HOURS env var (default 24h); <paramref name="StartupDelay"/> offsets the
/// first run past the catalog + album syncs so the three Deezer-heavy passes don't contend on boot.
/// Read in <see cref="MainModule"/> so the service stays env-free and unit-testable.
/// </summary>
public record ReplenishConfig(TimeSpan Interval, TimeSpan StartupDelay);
