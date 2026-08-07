namespace Mycelium.Backend;

/// <summary>
/// When a thumbed-down artist is worth offering back for reconsideration, and how often to go looking.
///
/// <paramref name="MinAverage"/> is the average star rating (0–5) the rated songs must reach, and
/// <paramref name="MinRatedFraction"/> the share of the artist's tracks they must actually have rated —
/// the second guard is what stops a single 5★ song on a 40-track discography from reading as "they
/// liked this band". <paramref name="Interval"/> is the sweep cadence: this exists to resurrect artists
/// buried years ago, so it's a slow background pass (default weekly), never a per-request computation.
/// <paramref name="StartupDelay"/> offsets the first run past the catalog + album syncs so the boot
/// isn't three Plex/Deezer-heavy passes at once.
///
/// Read from the RECONSIDER_MIN_AVG_STARS / RECONSIDER_MIN_RATED_FRACTION /
/// RECONSIDER_SWEEP_INTERVAL_DAYS env vars in <see cref="MainModule"/>, so the thresholds are
/// configurable and the sweep stays env-free and unit-testable.
/// </summary>
public record ReconsiderPolicy(
    double MinAverage,
    double MinRatedFraction,
    TimeSpan Interval,
    TimeSpan StartupDelay)
{
    /// <summary>
    /// Whether these ratings clear both bars. False when the artist isn't in Plex, has no tracks, or
    /// has nothing rated — an unrated artist carries no signal either way, so it stays rejected.
    /// </summary>
    public bool Qualifies(Interfaces.ArtistRatingStats stats) =>
        stats.Present
        && stats.TrackCount > 0
        && stats.RatedCount > 0
        && stats.Average >= MinAverage
        && stats.RatedCount >= stats.TrackCount * MinRatedFraction;
}
