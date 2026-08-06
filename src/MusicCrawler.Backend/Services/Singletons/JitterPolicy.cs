namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Scatters every recurring wait in the app by a random ±fraction, and owns the periodic-loop shape
/// that uses it. Nothing here runs on an exact cadence: a fetch landing on the same second of every
/// minute, or the same minute of every hour, is a machine signature no listener produces, and the
/// jobs that repeat are exactly the ones talking to Deezer, MusicBrainz and Plex.
///
/// <see cref="Percent"/> comes from <c>TIMER_JITTER_PERCENT</c> (default 30, clamped to 0–90 — at 100
/// a wait could round to nothing, defeating the throttle it's meant to scatter). 0 restores exact
/// timing.
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
    /// Runs <paramref name="pass"/> forever on a jittered cadence — the replacement for a fixed
    /// <c>Observable.Timer(startDelay, interval)</c>. Both the initial delay and every gap after are
    /// scattered. Returns on cancellation (shutdown) rather than throwing. <paramref name="onWait"/>,
    /// when supplied, is handed each actual wait before it starts, so a caller can publish when it
    /// next expects to act.
    /// </summary>
    public async Task RunPeriodic(
        TimeSpan startDelay,
        TimeSpan interval,
        Func<Task> pass,
        CancellationToken ct,
        Action<TimeSpan>? onWait = null)
    {
        try
        {
            var initial = Apply(startDelay);
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
                var wait = Apply(interval);
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
