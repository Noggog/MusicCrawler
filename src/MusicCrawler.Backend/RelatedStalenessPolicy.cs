namespace MusicCrawler.Backend;

/// <summary>
/// How long a stored similarity-graph edge set is considered fresh before re-ingestion is due.
/// Read from the RELATED_STALENESS_DAYS env var (default 30 days) in <see cref="MainModule"/>;
/// injected into the ingestion service so the window is configurable and unit-testable.
/// </summary>
public record RelatedStalenessPolicy(TimeSpan Window);
