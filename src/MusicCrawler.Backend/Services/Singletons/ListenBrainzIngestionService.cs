using Microsoft.Extensions.Logging;
using MusicCrawler.Interfaces;
using MusicCrawler.ListenBrainz.Inputs;
using MusicCrawler.ListenBrainz.Services;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Second similarity source (alongside <see cref="SimilarityIngestionService"/>): resolves an
/// artist to its MusicBrainz MBID, fetches ListenBrainz's similar artists for it, and persists them
/// into the graph tagged "listenbrainz". Same staleness/persistence contract as the Deezer source;
/// unlike it there's no image backfill (ListenBrainz returns no artwork) and an MBID hop up front.
/// </summary>
public class ListenBrainzIngestionService : ISimilaritySource
{
    public string SourceName => "listenbrainz";

    private readonly IListenBrainzApi _listenBrainz;
    private readonly MusicBrainzArtistResolver _resolver;
    private readonly IRelatedArtistRepo _repo;
    private readonly bool _enabled;
    private readonly ILogger<ListenBrainzIngestionService> _logger;
    private readonly TimeSpan _staleness;

    public ListenBrainzIngestionService(
        IListenBrainzApi listenBrainz,
        MusicBrainzArtistResolver resolver,
        IRelatedArtistRepo repo,
        ListenBrainzEndpointInfo endpointInfo,
        RelatedStalenessPolicy stalenessPolicy,
        ILogger<ListenBrainzIngestionService> logger)
    {
        _listenBrainz = listenBrainz;
        _resolver = resolver;
        _repo = repo;
        _enabled = endpointInfo.Enabled;
        _logger = logger;
        _staleness = stalenessPolicy.Window;
    }

    public async Task<ArtistRelations> EnsureRelated(ArtistKey artist, bool forceRefresh = false)
    {
        var existing = await _repo.Get(artist, SourceName);

        // Off-switch: never touch the network, just serve whatever (if anything) was ingested before.
        if (!_enabled)
        {
            return existing ?? Empty(artist);
        }

        if (!forceRefresh && existing != null && DateTimeOffset.UtcNow - existing.FetchedAt < _staleness)
        {
            return existing;
        }

        var identity = await _resolver.ResolveIdentity(artist.ArtistName);
        if (identity == null)
        {
            // No MBID (or MusicBrainz unreachable). Don't persist an empty result — it'd suppress
            // retries on a transient failure — serve whatever we already have.
            _logger.LogWarning("MusicBrainz had no MBID for {Artist}; keeping existing edges", artist.ArtistName);
            return existing ?? Empty(artist);
        }

        var related = (await _listenBrainz.GetSimilarArtists(identity.Mbid))
            // The endpoint includes the seed itself; drop it (by MBID) along with any nameless rows.
            .Where(r => !string.IsNullOrWhiteSpace(r.Name)
                        && !string.Equals(r.ArtistMbid, identity.Mbid, StringComparison.OrdinalIgnoreCase))
            .Select(r => new RelatedArtist(new ArtistKey(r.Name!), ImageUrl: null))
            .ToArray();

        var relations = new ArtistRelations(artist, SourceName, related, DateTimeOffset.UtcNow);
        await _repo.Upsert(relations);

        _logger.LogInformation(
            "Ingested {Count} ListenBrainz related artists for {Artist}", related.Length, artist.ArtistName);
        return relations;
    }

    private ArtistRelations Empty(ArtistKey artist) =>
        new(artist, SourceName, Array.Empty<RelatedArtist>(), DateTimeOffset.UtcNow);
}
