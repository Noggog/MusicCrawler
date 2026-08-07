namespace MusicCrawler.Backend;

/// <summary>
/// When the two daily Plex/Deezer passes run. Anchored to a wall-clock hour rather than to "24h after
/// boot": Plex only re-files newly-arrived music on its own nightly pass, so a catalog re-read that
/// drifted to just before that pass would miss a download by a whole extra day. Set from
/// <c>DAILY_SYNC_HOUR</c> (default 6, server-local — set <c>TZ</c> on the container) in
/// <see cref="MainModule"/> so the services stay env-free and unit-testable.
/// </summary>
/// <param name="CatalogSync">Plex library re-read (feeds everything else, so it goes first).</param>
/// <param name="AlbumSync">Deezer discography diff, offset past the catalog read so it works from a
/// fresh library rather than racing it.</param>
public record DailySyncSchedule(TimeOnly CatalogSync, TimeOnly AlbumSync);
