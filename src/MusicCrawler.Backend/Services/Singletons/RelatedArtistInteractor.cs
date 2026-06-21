using Microsoft.Extensions.Logging;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// The unified, cross-source related-artists read path. Abstracted so consumers (e.g. the
/// discovery engine) can depend on the read without the ingestion/unification machinery behind it.
/// </summary>
public interface IRelatedArtistReader
{
    Task<UnifiedRelations> GetRelated(ArtistKey artist, bool forceRefresh = false);
}

/// <summary>
/// Read path for related artists: ensures the graph is populated for an artist (ingesting on a
/// cache miss/stale), then unifies every source into a single list — one entry per distinct
/// artist, tagged with which sources recommended it — so the end user sees all the options.
/// </summary>
public class RelatedArtistInteractor : IRelatedArtistReader
{
    private readonly IEnumerable<ISimilaritySource> _sources;
    private readonly IRelatedArtistRepo _repo;
    private readonly ILogger<RelatedArtistInteractor> _logger;

    public RelatedArtistInteractor(
        IEnumerable<ISimilaritySource> sources,
        IRelatedArtistRepo repo,
        ILogger<RelatedArtistInteractor> logger)
    {
        _sources = sources;
        _repo = repo;
        _logger = logger;
    }

    public async Task<UnifiedRelations> GetRelated(ArtistKey artist, bool forceRefresh = false)
    {
        // Ensure every registered source's edges are present/fresh; the unify step below then merges
        // however many are stored. Sources are isolated: one throwing (a bug past its own graceful
        // degradation) must not deny the user the others' recommendations.
        foreach (var source in _sources)
        {
            try
            {
                await source.EnsureRelated(artist, forceRefresh);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Source {Source} failed to ingest related artists for {Artist}",
                    source.SourceName, artist.ArtistName);
            }
        }

        var perSource = await _repo.GetAllSources(artist);
        return new UnifiedRelations(artist, RelatedArtistUnifier.Unify(perSource));
    }
}
