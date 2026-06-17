using MusicCrawler.Deezer.Services;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Fetches Deezer's related artists for an artist and persists them into the similarity graph,
/// skipping artists fetched within the staleness window (see <see cref="RelatedStalenessPolicy"/>).
/// Folds in the image-backfill bonus: Deezer returns artist images, so the same pass fills the
/// ArtistImageUrl the Plex sync leaves null (for any artist already in the catalog).
/// </summary>
public class SimilarityIngestionService
{
    /// <summary>Source tag stored on every edge this service writes.</summary>
    public const string SourceName = "deezer";

    private readonly IDeezerApi _deezerApi;
    private readonly IRelatedArtistRepo _repo;
    private readonly IArtistCatalogRepo _catalog;
    private readonly ILogger<SimilarityIngestionService> _logger;
    private readonly TimeSpan _staleness;

    public SimilarityIngestionService(
        IDeezerApi deezerApi,
        IRelatedArtistRepo repo,
        IArtistCatalogRepo catalog,
        RelatedStalenessPolicy stalenessPolicy,
        ILogger<SimilarityIngestionService> logger)
    {
        _deezerApi = deezerApi;
        _repo = repo;
        _catalog = catalog;
        _logger = logger;
        _staleness = stalenessPolicy.Window;
    }

    /// <summary>
    /// Ensures the Deezer edge set for <paramref name="artist"/> is present and fresh, fetching
    /// and persisting it if missing/stale (or always, when <paramref name="forceRefresh"/>).
    /// Returns the current edge set (possibly empty if Deezer is unreachable and nothing is cached).
    /// </summary>
    public async Task<ArtistRelations> EnsureRelated(ArtistKey artist, bool forceRefresh = false)
    {
        var existing = await _repo.Get(artist, SourceName);

        if (!forceRefresh && existing != null && DateTimeOffset.UtcNow - existing.FetchedAt < _staleness)
        {
            return existing;
        }

        var deezerArtist = await _deezerApi.SearchArtist(artist.ArtistName);
        if (deezerArtist == null)
        {
            // No match or Deezer unreachable. Don't persist an empty result (it'd suppress
            // retries on a transient failure) — serve whatever we already have.
            _logger.LogWarning("Deezer had no artist for {Artist}; keeping existing edges", artist.ArtistName);
            return existing ?? new ArtistRelations(artist, SourceName, Array.Empty<RelatedArtist>(), DateTimeOffset.UtcNow);
        }

        var related = (await _deezerApi.GetRelated(deezerArtist.id))
            .Where(r => !string.IsNullOrWhiteSpace(r.name))
            .Select(r => new RelatedArtist(new ArtistKey(r.name!), r.BestImageUrl))
            .ToArray();

        var relations = new ArtistRelations(artist, SourceName, related, DateTimeOffset.UtcNow);
        await _repo.Upsert(relations);

        await BackfillImages(artist, deezerArtist.BestImageUrl, related);

        _logger.LogInformation("Ingested {Count} Deezer related artists for {Artist}", related.Length, artist.ArtistName);
        return relations;
    }

    private async Task BackfillImages(ArtistKey artist, string? artistImage, IReadOnlyList<RelatedArtist> related)
    {
        // The seed artist (in the library) plus any related artist that's also in the library get
        // their image filled. BackfillImages only touches artists already cataloged, so passing
        // related artists that aren't in the library is a harmless no-op.
        var backfill = new List<ArtistMetadata> { new(artist, artistImage) };
        backfill.AddRange(related
            .Where(r => r.ImageUrl != null)
            .Select(r => new ArtistMetadata(r.ArtistKey, r.ImageUrl)));

        var updated = await _catalog.BackfillImages(backfill);
        if (updated > 0)
        {
            _logger.LogInformation("Backfilled {Count} artist images from Deezer", updated);
        }
    }
}
