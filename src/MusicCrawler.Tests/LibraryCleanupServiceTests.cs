using FluentAssertions;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class LibraryCleanupServiceTests
{
    private const string Combined = "Nina Simone;Hot Chip";

    private readonly IArtistCatalogRepo _catalog = Substitute.For<IArtistCatalogRepo>();
    private readonly IUserQueueRepo _queue = Substitute.For<IUserQueueRepo>();
    private readonly IUserAlbumRatingRepo _albumRatings = Substitute.For<IUserAlbumRatingRepo>();
    private readonly LibraryCleanupService _sut;

    public LibraryCleanupServiceTests()
    {
        _sut = new LibraryCleanupService(_catalog, _queue, _albumRatings);

        // Empty defaults; each test fills what it needs.
        _catalog.FindCombinedArtistNames().Returns(Array.Empty<string>());
        _queue.FindCombinedRatings().Returns(Array.Empty<CombinedArtistVerdict>());
        _albumRatings.FindCombinedRatings().Returns(Array.Empty<CombinedAlbumVerdict>());
    }

    [Fact]
    public async Task Scan_lists_each_source_with_its_split_preview()
    {
        _catalog.FindCombinedArtistNames().Returns(new[] { Combined });
        _queue.FindCombinedRatings().Returns(new[]
        {
            new CombinedArtistVerdict("u1", Combined, DiscoveryStatus.Liked, "img"),
            new CombinedArtistVerdict("u2", Combined, DiscoveryStatus.Liked, null), // same name, 2nd user
        });
        _albumRatings.FindCombinedRatings().Returns(new[]
        {
            new CombinedAlbumVerdict("u1", Combined, "Some Album", "art", DiscoveryStatus.Liked),
        });

        var entries = await _sut.Scan();

        entries.Should().HaveCount(3);
        entries.Should().ContainSingle(e => e.Scope == "catalog")
            .Which.SplitInto.Should().Equal("Nina Simone", "Hot Chip");
        // Artist ratings aggregate across users: one entry covering both.
        entries.Should().ContainSingle(e => e.Scope == "artistRating")
            .Which.Affected.Should().Be(2);
        entries.Should().ContainSingle(e => e.Scope == "albumRating" && e.Album == "Some Album");
    }

    [Fact]
    public async Task Resolve_splits_catalog_entries_into_their_parts()
    {
        _catalog.FindCombinedArtistNames().Returns(new[] { Combined });

        var result = await _sut.Resolve();

        result.CatalogSplit.Should().Be(1);
        await _catalog.Received(1).SplitCombinedArtist(
            Combined,
            Arg.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { "Nina Simone", "Hot Chip" })),
            Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Resolve_re_attributes_an_artist_verdict_to_each_real_artist_then_drops_the_combined()
    {
        _queue.FindCombinedRatings().Returns(new[]
        {
            new CombinedArtistVerdict("u1", Combined, DiscoveryStatus.Liked, "img"),
        });

        var result = await _sut.Resolve();

        result.ArtistRatingsSplit.Should().Be(1);
        await _queue.Received(1).Rate("u1", "Nina Simone", DiscoveryStatus.Liked, "img");
        await _queue.Received(1).Rate("u1", "Hot Chip", DiscoveryStatus.Liked, "img");
        await _queue.Received(1).ClearVerdict("u1", Combined);
    }

    [Fact]
    public async Task Resolve_drops_pending_combined_rows_without_re_creating_them()
    {
        _queue.FindCombinedRatings().Returns(new[]
        {
            new CombinedArtistVerdict("u1", Combined, DiscoveryStatus.Pending, null),
        });

        var result = await _sut.Resolve();

        result.PendingRemoved.Should().Be(1);
        result.ArtistRatingsSplit.Should().Be(0);
        await _queue.Received(1).ClearVerdict("u1", Combined);
        await _queue.DidNotReceive().Rate(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DiscoveryStatus>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Resolve_re_attributes_an_album_verdict_to_each_real_artist_then_clears_the_combined()
    {
        _albumRatings.FindCombinedRatings().Returns(new[]
        {
            new CombinedAlbumVerdict("u1", Combined, "Some Album", "art", DiscoveryStatus.Liked),
        });

        var result = await _sut.Resolve();

        result.AlbumRatingsSplit.Should().Be(1);
        await _albumRatings.Received(1).Rate("u1", "Nina Simone", "Some Album", "art", DiscoveryStatus.Liked);
        await _albumRatings.Received(1).Rate("u1", "Hot Chip", "Some Album", "art", DiscoveryStatus.Liked);
        await _albumRatings.Received(1).Clear("u1", Combined, "Some Album");
    }

    [Fact]
    public async Task Resolve_does_nothing_when_there_is_nothing_to_clean()
    {
        var result = await _sut.Resolve();

        result.Should().Be(new CleanupResult(0, 0, 0, 0));
        await _catalog.DidNotReceive().SplitCombinedArtist(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<DateTimeOffset>());
    }
}
