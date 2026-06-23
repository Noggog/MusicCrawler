using Microsoft.Extensions.Logging;
using MusicCrawler.Interfaces;
using MusicCrawler.Plex.Services.Singletons;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Summarises the user's per-song Plex ratings for one artist — highest, lowest and average across the
/// songs they've actually rated — for the discovery readout. Plex only has songs for artists already in
/// the library, so an artist the catalog has no Plex rating keys for reports <see cref="ArtistRatingStats.Present"/>
/// false and the UI shows nothing. Ratings come back on Plex's 0–10 scale; we halve them to the 0–5
/// stars the user sees in Plex. A name can map to several Plex rating keys (split collaborators / recurring
/// names), so tracks are unioned across all of them. Auto-registers via the assembly scan, like
/// <see cref="LibrarySourcesService"/>.
/// </summary>
public class ArtistRatingStatsService
{
    private readonly IArtistCatalogRepo _catalog;
    private readonly IPlexApi _plex;
    private readonly ILogger<ArtistRatingStatsService> _logger;

    public ArtistRatingStatsService(
        IArtistCatalogRepo catalog, IPlexApi plex, ILogger<ArtistRatingStatsService> logger)
    {
        _catalog = catalog;
        _plex = plex;
        _logger = logger;
    }

    public async Task<ArtistRatingStats> Get(ArtistKey artist)
    {
        var keys = await _catalog.GetPlexRatingKeys(artist);
        if (keys.Count == 0)
        {
            // Not in Plex (e.g. a brand-new recommended artist) — there are no songs to summarise.
            return new ArtistRatingStats(artist, Present: false, null, null, null, RatedCount: 0, TrackCount: 0);
        }

        var tracks = new List<PlexTrack>();
        foreach (var key in keys)
        {
            try
            {
                tracks.AddRange(await _plex.GetArtistTracks(key));
            }
            catch (Exception ex)
            {
                // A flaky/unreachable Plex shouldn't fail the readout: report presence without stats.
                _logger.LogWarning(ex, "Couldn't fetch Plex tracks for {Artist} (key {Key})", artist.ArtistName, key);
            }
        }

        // Plex leaves an unrated song's userRating null (some server versions report 0); count only real
        // ratings, and convert the 0–10 scale to the 0–5 stars shown in the Plex UI.
        var ratings = tracks
            .Where(t => t.UserRating is > 0)
            .Select(t => t.UserRating!.Value / 2.0)
            .ToArray();

        if (ratings.Length == 0)
        {
            return new ArtistRatingStats(artist, Present: true, null, null, null, RatedCount: 0, TrackCount: tracks.Count);
        }

        return new ArtistRatingStats(
            artist,
            Present: true,
            Highest: ratings.Max(),
            Lowest: ratings.Min(),
            Average: Math.Round(ratings.Average(), 2),
            RatedCount: ratings.Length,
            TrackCount: tracks.Count);
    }
}
