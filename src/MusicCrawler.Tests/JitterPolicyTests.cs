using FluentAssertions;
using MusicCrawler.Backend.Services.Singletons;
using Xunit;

namespace MusicCrawler.Tests;

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
