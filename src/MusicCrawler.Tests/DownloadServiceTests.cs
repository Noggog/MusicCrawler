using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MusicCrawler.Backend.Services.Background;
using MusicCrawler.Backend.Services.Download;
using MusicCrawler.Interfaces;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class DownloadServiceTests
{
    private readonly FakePurchaseRepo _repo = new();
    private readonly IDownloader _downloader = Substitute.For<IDownloader>();

    private DownloadService Sut()
    {
        var config = new DownloaderConfig(
            Automatic: false, DownloadDir: "", RipBinary: "rip", Quality: "2", FallbackQuality: "1",
            Codec: "", BatchSize: 10, ItemDelay: TimeSpan.Zero, BatchInterval: TimeSpan.Zero,
            DownloadTimeout: TimeSpan.FromMinutes(15));
        // PurchaseService is only used by the background loop, not the methods under test — null is fine.
        return new DownloadService(_repo, _downloader, config, purchases: null!, NullLogger<DownloadService>.Instance);
    }

    private static PurchaseItem Album(string artist, string album, long deezerId, PurchaseStatus status = PurchaseStatus.Pending) =>
        new(PurchaseKey.ForAlbum(artist, album), FeedKind.MissingAlbum, new ArtistKey(artist), album,
            null, 0, Array.Empty<string>(), status, DateTimeOffset.UtcNow, null, deezerId);

    private static PurchaseItem Artist(string artist, PurchaseStatus status = PurchaseStatus.Pending) =>
        new(PurchaseKey.ForArtist(artist), FeedKind.RecommendedArtist, new ArtistKey(artist), null,
            null, 0, Array.Empty<string>(), status, DateTimeOffset.UtcNow, null, null);

    // ---- ProcessOne (the consumer's per-item work) ----

    [Fact]
    public async Task Successful_download_marks_the_item_sent()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(true);
        var item = Album("Big Thief", "Capacity", 12345);
        _repo.Seed(item);

        var ran = await Sut().ProcessOne(item.Id);

        ran.Should().BeTrue();
        _repo.Items.Single().Status.Should().Be(PurchaseStatus.Sent);
    }

    [Fact]
    public async Task Failed_download_marks_the_item_failed()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(false);
        var item = Album("Big Thief", "Capacity", 12345);
        _repo.Seed(item);

        await Sut().ProcessOne(item.Id);

        _repo.Items.Single().Status.Should().Be(PurchaseStatus.Failed);
    }

    [Fact]
    public async Task A_thrown_downloader_is_caught_and_the_item_marked_failed()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns<bool>(_ => throw new InvalidOperationException("boom"));
        var item = Album("Big Thief", "Capacity", 12345);
        _repo.Seed(item);

        await Sut().ProcessOne(item.Id);

        _repo.Items.Single().Status.Should().Be(PurchaseStatus.Failed);
    }

    [Fact]
    public async Task Non_pending_or_non_downloadable_items_are_skipped()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(true);
        _repo.Seed(Album("A", "already-sent", 1, PurchaseStatus.Sent)); // not pending
        _repo.Seed(Album("B", "no-id", 0));                              // no deezer id
        _repo.Seed(Artist("Phoebe Bridgers"));                          // artist, not an album

        (await Sut().ProcessOne(PurchaseKey.ForAlbum("A", "already-sent"))).Should().BeFalse();
        (await Sut().ProcessOne(PurchaseKey.ForAlbum("B", "no-id"))).Should().BeFalse();
        (await Sut().ProcessOne(PurchaseKey.ForArtist("Phoebe Bridgers"))).Should().BeFalse();

        await _downloader.DidNotReceive().Request(Arg.Any<PurchaseItem>());
    }

    // ---- RequestDownload (the manual "Download now" trigger) ----

    [Fact]
    public async Task Manual_request_resets_a_failed_album_to_pending()
    {
        _repo.Seed(Album("Big Thief", "Capacity", 12345, PurchaseStatus.Failed));

        (await Sut().RequestDownload(PurchaseKey.ForAlbum("Big Thief", "Capacity"))).Should().BeTrue();

        _repo.Items.Single().Status.Should().Be(PurchaseStatus.Pending);
    }

    [Fact]
    public async Task Manual_request_rejects_artists_and_unknown_ids()
    {
        _repo.Seed(Artist("Phoebe Bridgers"));

        (await Sut().RequestDownload(PurchaseKey.ForArtist("Phoebe Bridgers"))).Should().BeFalse();
        (await Sut().RequestDownload("nope")).Should().BeFalse();
    }

    // ---- Crash recovery ----

    [Fact]
    public async Task Reset_returns_stranded_downloads_to_pending()
    {
        _repo.Seed(Album("Big Thief", "Capacity", 1, PurchaseStatus.Downloading));
        _repo.Seed(Album("Other", "Done", 2, PurchaseStatus.Sent));

        await Sut().ResetStuckDownloads();

        _repo.Items.Single(i => i.Album == "Capacity").Status.Should().Be(PurchaseStatus.Pending);
        _repo.Items.Single(i => i.Album == "Done").Status.Should().Be(PurchaseStatus.Sent);
    }
}
