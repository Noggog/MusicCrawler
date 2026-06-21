namespace MusicCrawler.Interfaces;

/// <summary>
/// One source of the similarity graph (Deezer, ListenBrainz, ...). Each implementation owns a
/// distinct <see cref="SourceName"/> tag and knows how to fetch + persist its own edge set for an
/// artist. The read path iterates every registered source to populate the graph, then unifies the
/// per-source edges at read time — so adding a source is just adding an implementation, with no
/// change to the reader or the unifier.
/// </summary>
public interface ISimilaritySource
{
    /// <summary>The tag stored on every edge this source writes (e.g. "deezer", "listenbrainz").</summary>
    string SourceName { get; }

    /// <summary>
    /// Ensures this source's edge set for <paramref name="artist"/> is present and fresh, fetching
    /// and persisting it if missing/stale (or always, when <paramref name="forceRefresh"/>). Returns
    /// the current edge set (possibly empty if the upstream is unreachable and nothing is cached).
    /// </summary>
    Task<ArtistRelations> EnsureRelated(ArtistKey artist, bool forceRefresh = false);
}
