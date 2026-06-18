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

    private DownloadService Sut(int batchSize = 10)
    {
        var config = new DownloaderConfig(
            Enabled: true, DownloadDir: "", RipBinary: "rip", Quality: "", Codec: "",
            BatchSize: batchSize, ItemDelay: TimeSpan.Zero, BatchInterval: TimeSpan.Zero);
        // purchases is only used by the background loop, not DrainBatch — null is fine for these tests.
        return new DownloadService(_repo, _downloader, config, purchases: null!, NullLogger<DownloadService>.Instance);
    }

    private static PurchaseItem Album(string artist, string album, long deezerId, PurchaseStatus status = PurchaseStatus.Pending) =>
        new(PurchaseKey.ForAlbum(artist, album), FeedKind.MissingAlbum, new ArtistKey(artist), album,
            null, 0, Array.Empty<string>(), status, DateTimeOffset.UtcNow, null, deezerId);

    private static PurchaseItem Artist(string artist) =>
        new(PurchaseKey.ForArtist(artist), FeedKind.RecommendedArtist, new ArtistKey(artist), null,
            null, 0, Array.Empty<string>(), PurchaseStatus.Pending, DateTimeOffset.UtcNow, null, null);

    [Fact]
    public async Task Successful_download_marks_the_item_sent()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(true);
        _repo.Seed(Album("Big Thief", "Capacity", 12345));

        var processed = await Sut().DrainBatch(CancellationToken.None);

        processed.Should().Be(1);
        _repo.Items.Single().Status.Should().Be(PurchaseStatus.Sent);
    }

    [Fact]
    public async Task Failed_download_marks_the_item_failed()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(false);
        _repo.Seed(Album("Big Thief", "Capacity", 12345));

        await Sut().DrainBatch(CancellationToken.None);

        _repo.Items.Single().Status.Should().Be(PurchaseStatus.Failed);
    }

    [Fact]
    public async Task A_thrown_downloader_is_caught_and_the_item_marked_failed()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns<bool>(_ => throw new InvalidOperationException("boom"));
        _repo.Seed(Album("Big Thief", "Capacity", 12345));

        await Sut().DrainBatch(CancellationToken.None);

        _repo.Items.Single().Status.Should().Be(PurchaseStatus.Failed);
    }

    [Fact]
    public async Task Artists_and_albums_without_a_deezer_id_are_not_downloaded()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(true);
        _repo.Seed(Artist("Phoebe Bridgers"));
        _repo.Seed(Album("Big Thief", "No Id", 0));

        var processed = await Sut().DrainBatch(CancellationToken.None);

        processed.Should().Be(0);
        await _downloader.DidNotReceive().Request(Arg.Any<PurchaseItem>());
        _repo.Items.Should().OnlyContain(i => i.Status == PurchaseStatus.Pending);
    }

    [Fact]
    public async Task Only_non_pending_albums_are_left_alone()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(true);
        _repo.Seed(Album("A", "sent", 1, PurchaseStatus.Sent));
        _repo.Seed(Album("B", "pending", 2));

        var processed = await Sut().DrainBatch(CancellationToken.None);

        processed.Should().Be(1);
        await _downloader.Received(1).Request(Arg.Is<PurchaseItem>(p => p.Album == "pending"));
    }

    [Fact]
    public async Task Batch_size_caps_how_many_download_per_pass()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(true);
        _repo.Seed(Album("A", "one", 1));
        _repo.Seed(Album("B", "two", 2));
        _repo.Seed(Album("C", "three", 3));

        var processed = await Sut(batchSize: 2).DrainBatch(CancellationToken.None);

        processed.Should().Be(2);
        _repo.Items.Count(i => i.Status == PurchaseStatus.Sent).Should().Be(2);
        _repo.Items.Count(i => i.Status == PurchaseStatus.Pending).Should().Be(1);
    }
}
