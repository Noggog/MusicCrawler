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
    private readonly SimilarityIngestionService _ingestion;
    private readonly IRelatedArtistRepo _repo;

    public RelatedArtistInteractor(SimilarityIngestionService ingestion, IRelatedArtistRepo repo)
    {
        _ingestion = ingestion;
        _repo = repo;
    }

    public async Task<UnifiedRelations> GetRelated(ArtistKey artist, bool forceRefresh = false)
    {
        // Currently only Deezer ingests; as more sources are added, each gets its own ensure call
        // here while the unify step below already merges however many sources are stored.
        await _ingestion.EnsureRelated(artist, forceRefresh);

        var perSource = await _repo.GetAllSources(artist);
        return new UnifiedRelations(artist, RelatedArtistUnifier.Unify(perSource));
    }
}
