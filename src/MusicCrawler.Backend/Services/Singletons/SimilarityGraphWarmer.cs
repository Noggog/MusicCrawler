using Microsoft.Extensions.Logging;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>A point-in-time view of a whole-catalog similarity warm, for the dev panel's progress UI.</summary>
public record SimilarityWarmStatus(
    bool Running,
    int Processed,
    int Total,
    int Errors,
    string? CurrentArtist,
    bool ForceRefresh,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Dev tool: warms the similarity graph for the <em>entire</em> library, not just one user's liked
/// artists. Walks every present catalog artist and runs the normal read path (<see
/// cref="IRelatedArtistReader.GetRelated"/>), which ensures + persists every registered source's
/// edges (Deezer, ListenBrainz, …) — so the lazy, usage-driven population can be forced all at once.
///
/// Runs as a single-flight background job (mirrors <c>DownloadService</c>): the endpoint kicks it off
/// and returns immediately, because a full sweep is bounded by MusicBrainz's ~1 req/s and can take
/// many minutes. Progress is polled via <see cref="GetStatus"/>. Default is gap-fill (skips edges
/// still fresh per source); force re-fetches everything, fresh or not.
/// </summary>
public class SimilarityGraphWarmer
{
    private readonly IArtistCatalogRepo _catalog;
    private readonly IRelatedArtistReader _related;
    private readonly ILogger<SimilarityGraphWarmer> _logger;

    private readonly object _gate = new();
    private Task? _run;

    // Mutated by the background run, read (racily, which is fine for a progress display) by GetStatus.
    private volatile bool _running;
    private int _processed;
    private int _total;
    private int _errors;
    private volatile string? _currentArtist;
    private bool _forceRefresh;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;

    public SimilarityGraphWarmer(
        IArtistCatalogRepo catalog,
        IRelatedArtistReader related,
        ILogger<SimilarityGraphWarmer> logger)
    {
        _catalog = catalog;
        _related = related;
        _logger = logger;
    }

    /// <summary>
    /// Starts a warm if one isn't already running, then returns the current status. Idempotent while
    /// a run is in flight — a second click just returns the live progress instead of starting again.
    /// </summary>
    public SimilarityWarmStatus Start(bool forceRefresh)
    {
        lock (_gate)
        {
            if (!_running)
            {
                _running = true;
                _forceRefresh = forceRefresh;
                _processed = 0;
                _total = 0;
                _errors = 0;
                _currentArtist = null;
                _startedAt = DateTimeOffset.UtcNow;
                _finishedAt = null;
                // Fire-and-forget; held in a field so it isn't collected mid-run.
                _run = Task.Run(() => RunAsync(forceRefresh));
            }
            return Snapshot();
        }
    }

    public SimilarityWarmStatus GetStatus()
    {
        lock (_gate)
        {
            return Snapshot();
        }
    }

    private SimilarityWarmStatus Snapshot() => new(
        Running: _running,
        Processed: _processed,
        Total: _total,
        Errors: _errors,
        CurrentArtist: _currentArtist,
        ForceRefresh: _forceRefresh,
        StartedAt: _startedAt,
        FinishedAt: _finishedAt);

    private async Task RunAsync(bool forceRefresh)
    {
        try
        {
            var artists = await _catalog.GetAllPresent();
            _total = artists.Length;
            _logger.LogInformation(
                "Similarity warm started for {Total} artist(s) (forceRefresh={Force})", _total, forceRefresh);

            foreach (var artist in artists)
            {
                _currentArtist = artist.ArtistKey.ArtistName;
                try
                {
                    // Fans out to every source and persists; each source already degrades gracefully,
                    // and the reader isolates a thrown source — but guard here too so one bad artist
                    // can't abort the whole sweep.
                    await _related.GetRelated(artist.ArtistKey, forceRefresh);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _errors);
                    _logger.LogWarning(ex, "Similarity warm failed for {Artist}", artist.ArtistKey.ArtistName);
                }
                Interlocked.Increment(ref _processed);
            }

            _logger.LogInformation(
                "Similarity warm finished: {Processed}/{Total} processed, {Errors} error(s)",
                _processed, _total, _errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Similarity warm aborted");
        }
        finally
        {
            lock (_gate)
            {
                _currentArtist = null;
                _finishedAt = DateTimeOffset.UtcNow;
                _running = false;
            }
        }
    }
}
