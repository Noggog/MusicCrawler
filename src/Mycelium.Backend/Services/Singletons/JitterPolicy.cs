namespace Mycelium.Backend.Services.Singletons;

/// <summary>
/// Scatters a recurring wait by a random ±fraction, and owns the periodic-loop shapes that use it.
/// A fetch landing on the same second of every minute is a machine signature no listener produces —
/// so the loops that reach <b>third-party</b> services (Deezer, MusicBrainz), which have every reason
/// to fingerprint tooling, never run on an exact cadence.
///
/// Loops that only talk to the user's own Plex server and Mongo pass <c>scatter: false</c>: nothing
/// there is trying to spot a bot, and smearing those waits only makes a schedule vaguer than it needs
/// to be. They still come through here for the loop shape (cancellation, startup pass, the daily
/// anchor), not for the randomness.
///
/// <see cref="Percent"/> comes from <c>TIMER_JITTER_PERCENT</c> (default 30, clamped to 0–90 — at 100
/// a wait could round to nothing, defeating the throttle it's meant to scatter). 0 restores exact
/// timing everywhere.
/// </summary>
public class JitterPolicy
{
    private readonly double _fraction;

    public JitterPolicy(double fraction)
    {
        _fraction = Math.Clamp(fraction, 0, 0.9);
    }

    /// <summary>The spread as a percentage, for display in the download monitor.</summary>
    public double Percent => Math.Round(_fraction * 100);

    /// <summary>
    /// <paramref name="delay"/> scattered by up to ±<see cref="Percent"/> of itself, drawn afresh on
    /// every call so repeated waits never settle into a pattern. Zero delays and zero jitter pass
    /// through untouched — that's the "no throttle" path tests take, and it must not depend on the
    /// arithmetic.
    /// </summary>
    public TimeSpan Apply(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero || _fraction <= 0)
        {
            return delay;
        }

        // NextDouble() is [0,1) -> factor in [1 - fraction, 1 + fraction).
        var factor = 1 + ((Random.Shared.NextDouble() * 2) - 1) * _fraction;
        return delay * factor;
    }

    /// <summary>
    /// How far a fixed-time daily job may slip past its target at full jitter. Scaled by
    /// <see cref="Percent"/>, so the default ±30% lands the pass within ~9 minutes of the hour: enough
    /// that the fetch isn't on the same second every day, small enough that "6am" still means 6am.
    /// </summary>
    private static readonly TimeSpan DailySpread = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Wait until the next <paramref name="timeOfDay"/> (server-local wall clock). When
    /// <paramref name="scatter"/> is on it slips <em>forwards only</em>, by up to
    /// <see cref="DailySpread"/> × the configured fraction — forwards only because a pass that woke
    /// early would find its own target still ahead of it and run twice.
    /// </summary>
    internal TimeSpan UntilNextDaily(DateTime now, TimeOnly timeOfDay, bool scatter)
    {
        var target = now.Date + timeOfDay.ToTimeSpan();
        if (target <= now)
        {
            target = target.AddDays(1);
        }

        var wait = target - now;
        return scatter ? wait + (DailySpread * (Random.Shared.NextDouble() * _fraction)) : wait;
    }

    /// <summary>
    /// Runs <paramref name="pass"/> once after <paramref name="startupDelay"/>, then every day at
    /// <paramref name="timeOfDay"/> — for work whose usefulness is tied to a wall-clock hour rather
    /// than to an elapsed interval (the Plex catalog only re-files new music on its own nightly pass,
    /// so re-reading it 20 minutes before that happens wastes a whole day). Recomputed from the local
    /// clock each cycle, so it self-corrects across DST rather than drifting an hour.
    /// <paramref name="scatter"/> is for the callers that hit a third party — see the type summary.
    /// </summary>
    public async Task RunDaily(
        TimeOnly timeOfDay,
        TimeSpan startupDelay,
        Func<Task> pass,
        CancellationToken ct,
        bool scatter)
    {
        try
        {
            var initial = scatter ? Apply(startupDelay) : startupDelay;
            if (initial > TimeSpan.Zero)
            {
                await Task.Delay(initial, ct);
            }

            while (!ct.IsCancellationRequested)
            {
                await pass();
                await Task.Delay(UntilNextDaily(DateTime.Now, timeOfDay, scatter), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    /// <summary>
    /// Runs <paramref name="pass"/> forever on a fixed cadence — the replacement for a fixed
    /// <c>Observable.Timer(startDelay, interval)</c>. With <paramref name="scatter"/> on (the default,
    /// for the third-party-facing loops) the initial delay and every gap after are scattered. Returns
    /// on cancellation (shutdown) rather than throwing. <paramref name="onWait"/>, when supplied, is
    /// handed each actual wait before it starts, so a caller can publish when it next expects to act.
    /// </summary>
    public async Task RunPeriodic(
        TimeSpan startDelay,
        TimeSpan interval,
        Func<Task> pass,
        CancellationToken ct,
        Action<TimeSpan>? onWait = null,
        bool scatter = true)
    {
        try
        {
            var initial = scatter ? Apply(startDelay) : startDelay;
            if (initial > TimeSpan.Zero)
            {
                onWait?.Invoke(initial);
                await Task.Delay(initial, ct);
            }

            while (!ct.IsCancellationRequested)
            {
                await pass();

                // A floor, not a throttle: a misconfigured interval of zero would otherwise spin this
                // loop against the network as fast as the CPU allows.
                var wait = scatter ? Apply(interval) : interval;
                wait = wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
                onWait?.Invoke(wait);
                await Task.Delay(wait, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
