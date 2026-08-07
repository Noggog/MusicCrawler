using FluentAssertions;
using Mycelium.Backend.Services.Singletons;
using Xunit;

namespace Mycelium.Tests;

public class JitterPolicyTests
{
    private static readonly TimeSpan Nominal = TimeSpan.FromSeconds(60);

    [Fact]
    public void Waits_stay_within_the_configured_spread_and_actually_vary()
    {
        var policy = new JitterPolicy(0.3);

        var samples = Enumerable.Range(0, 200).Select(_ => policy.Apply(Nominal)).ToList();

        samples.Should().OnlyContain(d => d >= Nominal * 0.7 && d <= Nominal * 1.3);
        samples.Distinct().Should().HaveCountGreaterThan(100, "every wait should be drawn afresh");
        // Scattered around the nominal wait rather than clustered at one end — the average holds, so
        // scattering the cadence doesn't quietly change the throttle.
        samples.Average(d => d.TotalSeconds).Should().BeApproximately(60, 3);
    }

    [Fact]
    public void Zero_jitter_keeps_exact_timing()
    {
        new JitterPolicy(0).Apply(Nominal).Should().Be(Nominal);
    }

    [Fact]
    public void A_zero_delay_stays_zero_however_much_jitter_is_configured()
    {
        // Nothing to scatter, and a scaled zero would still be zero — but this is the path tests and a
        // "no throttle" configuration take, so it must not depend on the arithmetic.
        new JitterPolicy(0.9).Apply(TimeSpan.Zero).Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(-1, 0)]     // nonsense input can't invert the delay
    [InlineData(0.3, 30)]
    [InlineData(5, 90)]     // clamped: a full-width spread could round a wait to nothing
    public void The_spread_is_clamped_to_a_sane_range(double fraction, double expectedPercent)
    {
        new JitterPolicy(fraction).Percent.Should().Be(expectedPercent);
    }

    [Fact]
    public void The_daily_anchor_waits_until_the_hour_comes_round_again()
    {
        var policy = new JitterPolicy(0.3);
        var sixAm = new TimeOnly(6, 0);

        // Unscattered (the Plex-only callers): exactly on the hour, however much jitter is configured.
        // Before the hour: later today.
        policy.UntilNextDaily(new DateTime(2026, 8, 7, 4, 30, 0), sixAm, scatter: false)
            .Should().Be(TimeSpan.FromHours(1.5));
        // At or past it (a pass that just ran at its own target): tomorrow, not immediately again.
        policy.UntilNextDaily(new DateTime(2026, 8, 7, 6, 0, 0), sixAm, scatter: false)
            .Should().Be(TimeSpan.FromHours(24));
        policy.UntilNextDaily(new DateTime(2026, 8, 7, 23, 45, 0), sixAm, scatter: false)
            .Should().Be(TimeSpan.FromHours(6.25));
    }

    [Fact]
    public void A_scattered_daily_anchor_only_ever_slips_forwards()
    {
        var policy = new JitterPolicy(0.3);
        var sixAm = new TimeOnly(6, 0);
        var now = new DateTime(2026, 8, 7, 5, 55, 0);

        var samples = Enumerable.Range(0, 200)
            .Select(_ => policy.UntilNextDaily(now, sixAm, scatter: true))
            .ToList();

        // Never early: a pass that woke before its target would find the target still ahead and run
        // twice. Never more than the scaled spread late, so "6am" still means 6am.
        samples.Should().OnlyContain(
            w => w >= TimeSpan.FromMinutes(5) && w <= TimeSpan.FromMinutes(5 + 9));
        samples.Distinct().Should().HaveCountGreaterThan(100, "the slip is drawn afresh each day");
    }

    [Fact]
    public async Task RunDaily_runs_a_startup_pass_before_the_first_anchored_one()
    {
        var policy = new JitterPolicy(0);
        using var cts = new CancellationTokenSource();
        var passes = 0;

        // The next anchor is ~a day out, so the run only gets past its first pass because the startup
        // pass happens up front — that's what keeps a fresh deploy from serving a stale catalog.
        var run = policy.RunDaily(
            TimeOnly.FromDateTime(DateTime.Now).AddHours(12),
            TimeSpan.Zero,
            () =>
            {
                passes++;
                cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token,
            scatter: false);

        await run;
        passes.Should().Be(1);
    }

    [Fact]
    public async Task An_unscattered_periodic_loop_waits_exactly_its_interval()
    {
        var policy = new JitterPolicy(0.9);
        using var cts = new CancellationTokenSource();
        var waits = new List<TimeSpan>();

        await policy.RunPeriodic(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1),
            () =>
            {
                if (waits.Count == 2)
                {
                    cts.Cancel();
                }
                return Task.CompletedTask;
            },
            cts.Token,
            onWait: waits.Add,
            scatter: false);

        // Plex-only loops (the download settle pass) opt out — the configured spread doesn't touch them.
        var expected = TimeSpan.FromMilliseconds(1);
        waits.Should().NotBeEmpty();
        waits.Should().OnlyContain(w => w == expected);
    }

    [Fact]
    public async Task RunPeriodic_repeats_the_pass_until_cancelled()
    {
        var policy = new JitterPolicy(0.5);
        using var cts = new CancellationTokenSource();
        var passes = 0;

        await policy.RunPeriodic(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1),
            () =>
            {
                if (++passes == 3)
                {
                    cts.Cancel();
                }
                return Task.CompletedTask;
            },
            cts.Token);

        // Returns rather than throwing on cancellation — a hosted service's loop ending is a normal
        // shutdown, not a fault.
        passes.Should().Be(3);
    }

    [Fact]
    public async Task RunPeriodic_reports_each_wait_before_taking_it()
    {
        var policy = new JitterPolicy(0);
        using var cts = new CancellationTokenSource();
        var waits = new List<TimeSpan>();

        await policy.RunPeriodic(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1),
            () =>
            {
                if (waits.Count == 2)
                {
                    cts.Cancel();
                }
                return Task.CompletedTask;
            },
            cts.Token,
            onWait: waits.Add);

        // The callback is what lets the download monitor say when the next pass is due.
        var expected = TimeSpan.FromMilliseconds(1);
        waits.Should().NotBeEmpty();
        waits.Should().OnlyContain(w => w == expected);
    }
}
