using System.Reactive.Concurrency;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;
using MusicCrawler.Backend;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Plex;
using MusicCrawler.Plex.Services.Singletons;
using Xunit;

namespace MusicCrawler.Tests;

public class PlexLibraryScannerTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMinutes(5);

    // A scanner whose actual Plex hit is replaced by a counter, so we test the debounce/gate logic
    // without any HTTP, and on a TestScheduler so the debounce clock is deterministic (no Task.Delay).
    // The PlexApi instance only satisfies the base ctor — it's never touched (Scan() is overridden).
    private sealed class TestScanner : PlexLibraryScanner
    {
        public int ScanCount;

        public TestScanner(LibraryScannerConfig config, IScheduler scheduler)
            : base(
                new PlexApi(new PlexEndpointInfo("http://localhost"), new PlexClientInfo("token"),
                    NullLogger<PlexApi>.Instance),
                config,
                NullLogger<PlexLibraryScanner>.Instance,
                scheduler)
        {
        }

        protected override Task Scan()
        {
            Interlocked.Increment(ref ScanCount);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Disabled_scanner_never_scans()
    {
        var scheduler = new TestScheduler();
        var sut = new TestScanner(new LibraryScannerConfig(Enabled: false, Debounce), scheduler);

        await sut.RequestScan();
        scheduler.AdvanceBy(Debounce.Ticks * 2);

        sut.ScanCount.Should().Be(0);
    }

    [Fact]
    public async Task Enabled_scanner_scans_once_the_debounce_window_elapses()
    {
        var scheduler = new TestScheduler();
        var sut = new TestScanner(new LibraryScannerConfig(Enabled: true, Debounce), scheduler);

        await sut.RequestScan();
        sut.ScanCount.Should().Be(0);      // nothing until the quiet window passes

        scheduler.AdvanceBy(Debounce.Ticks + 1);
        sut.ScanCount.Should().Be(1);
    }

    [Fact]
    public async Task A_burst_of_requests_coalesces_into_a_single_scan()
    {
        // A draining batch fires many RequestScan calls in quick succession; the trailing debounce
        // folds them into exactly one scan once the window finally elapses.
        var scheduler = new TestScheduler();
        var sut = new TestScanner(new LibraryScannerConfig(Enabled: true, Debounce), scheduler);

        await sut.RequestScan();
        await sut.RequestScan();
        await sut.RequestScan();

        scheduler.AdvanceBy(Debounce.Ticks + 1);

        sut.ScanCount.Should().Be(1);
    }

    [Fact]
    public async Task A_later_batch_triggers_a_fresh_scan()
    {
        var scheduler = new TestScheduler();
        var sut = new TestScanner(new LibraryScannerConfig(Enabled: true, Debounce), scheduler);

        await sut.RequestScan();
        scheduler.AdvanceBy(Debounce.Ticks + 1);
        sut.ScanCount.Should().Be(1);

        // A second, later burst (after the first scan fired) produces its own scan.
        await sut.RequestScan();
        scheduler.AdvanceBy(Debounce.Ticks + 1);
        sut.ScanCount.Should().Be(2);
    }
}
