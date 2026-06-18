using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class PurchaseServiceTests
{
    private readonly FakePurchaseRepo _purchases = new();
    private readonly IUserQueueRepo _queue = Substitute.For<IUserQueueRepo>();
    private readonly IUserAlbumRatingRepo _albumRatings = Substitute.For<IUserAlbumRatingRepo>();
    private readonly ILibraryProvider _library = Substitute.For<ILibraryProvider>();
    private readonly IArtistCatalogRepo _catalog = Substitute.For<IArtistCatalogRepo>();
    private readonly IMissingAlbumRepo _missing = Substitute.For<IMissingAlbumRepo>();
    private readonly IDownloader _downloader = Substitute.For<IDownloader>();
    private readonly PurchaseService _sut;

    public PurchaseServiceTests()
    {
        _sut = new PurchaseService(
            _purchases, _queue, _albumRatings, _library, _catalog, _missing, _downloader,
            NullLogger<PurchaseService>.Instance);

        _queue.GetAllLiked().Returns(Array.Empty<DiscoveryCandidate>());
        _albumRatings.GetAllLiked().Returns(Array.Empty<AlbumRating>());
        _library.GetAllArtistMetadata().Returns(Array.Empty<ArtistMetadata>());
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        _missing.GetAll().Returns(Array.Empty<MissingAlbum>());
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(true);
    }

    private void Owned(params string[] artists) =>
        _library.GetAllArtistMetadata().Returns(artists.Select(a => new ArtistMetadata(new ArtistKey(a), null)).ToArray());

    [Fact]
    public async Task Active_lists_liked_non_owned_artists_and_still_missing_liked_albums()
    {
        Owned("Owned Band");
        _queue.GetAllLiked().Returns(new[]
        {
            new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), null, 3, new[] { "boygenius" }, 1),
            new DiscoveryCandidate(new ArtistKey("Owned Band"), null, 1, Array.Empty<string>(), 0), // owned -> excluded
        });
        _albumRatings.GetAllLiked().Returns(new[]
        {
            new AlbumRating(new ArtistKey("Owned Band"), new AlbumKey("New One"), "art", DiscoveryStatus.Liked),
        });

        var active = await _sut.GetActive();

        active.Where(p => p.Kind == FeedKind.RecommendedArtist).Select(p => p.Artist.ArtistName)
            .Should().Equal("Phoebe Bridgers");
        active.Where(p => p.Kind == FeedKind.MissingAlbum).Select(p => p.Album).Should().Equal("New One");
        active.Should().OnlyContain(p => p.Status == PurchaseStatus.Pending);
    }

    [Fact]
    public async Task Active_dedups_items_liked_by_multiple_users()
    {
        // Same artist liked by two users: one occurrence, strongest score, unioned sources.
        _queue.GetAllLiked().Returns(new[]
        {
            new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), null, 3, new[] { "boygenius" }, 1),
            new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), "img", 5, new[] { "Bright Eyes" }, 1),
        });
        _albumRatings.GetAllLiked().Returns(new[]
        {
            new AlbumRating(new ArtistKey("Big Thief"), new AlbumKey("Capacity"), "art", DiscoveryStatus.Liked),
            new AlbumRating(new ArtistKey("Big Thief"), new AlbumKey("Capacity"), "art2", DiscoveryStatus.Liked),
        });

        var active = await _sut.GetActive();

        var artist = active.Single(p => p.Kind == FeedKind.RecommendedArtist);
        artist.Artist.ArtistName.Should().Be("Phoebe Bridgers");
        artist.Score.Should().Be(5);
        artist.ImageUrl.Should().Be("img");
        artist.Sources.Should().BeEquivalentTo("boygenius", "Bright Eyes");

        active.Where(p => p.Kind == FeedKind.MissingAlbum).Select(p => p.Album).Should().Equal("Capacity");
    }

    [Fact]
    public async Task Ordering_routes_through_the_downloader_and_marks_sent()
    {
        _queue.GetAllLiked().Returns(new[]
        {
            new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), null, 3, Array.Empty<string>(), 1),
        });
        await _sut.Reconcile();
        var id = PurchaseKey.ForArtist("Phoebe Bridgers");

        var ok = await _sut.Order(id);

        ok.Should().BeTrue();
        await _downloader.Received(1).Request(Arg.Is<PurchaseItem>(p => p.Id == id));
        (await _sut.GetActive()).Single().Status.Should().Be(PurchaseStatus.Sent);
    }

    [Fact]
    public async Task Declined_order_leaves_the_item_pending()
    {
        _downloader.Request(Arg.Any<PurchaseItem>()).Returns(false);
        _queue.GetAllLiked().Returns(new[]
        {
            new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), null, 3, Array.Empty<string>(), 1),
        });
        await _sut.Reconcile();
        var id = PurchaseKey.ForArtist("Phoebe Bridgers");

        var ok = await _sut.Order(id);

        ok.Should().BeFalse();
        (await _sut.GetActive()).Single().Status.Should().Be(PurchaseStatus.Pending);
    }

    [Fact]
    public async Task Pending_item_no_longer_liked_is_pruned_but_an_ordered_one_is_kept()
    {
        var liked = new[]
        {
            new DiscoveryCandidate(new ArtistKey("Pending Band"), null, 1, Array.Empty<string>(), 1),
            new DiscoveryCandidate(new ArtistKey("Ordered Band"), null, 1, Array.Empty<string>(), 1),
        };
        _queue.GetAllLiked().Returns(liked);
        await _sut.Reconcile();
        await _sut.Order(PurchaseKey.ForArtist("Ordered Band"));

        // Both un-liked now (nobody wants them via ratings any more).
        _queue.GetAllLiked().Returns(Array.Empty<DiscoveryCandidate>());
        var active = await _sut.GetActive();

        // The pending one is dropped; the already-ordered one survives (it's in flight).
        active.Select(p => p.Artist.ArtistName).Should().Equal("Ordered Band");
        active.Single().Status.Should().Be(PurchaseStatus.Sent);
    }

    [Fact]
    public async Task Acquired_artist_closes_out_to_in_library_and_drops_off()
    {
        _queue.GetAllLiked().Returns(new[]
        {
            new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), null, 3, Array.Empty<string>(), 1),
        });
        await _sut.Reconcile();

        // It's now in the library (and still liked).
        Owned("Phoebe Bridgers");
        var active = await _sut.GetActive();

        active.Should().BeEmpty();
        _purchases.Items.Single().Status.Should().Be(PurchaseStatus.InLibrary);
    }

    [Fact]
    public async Task Album_items_carry_the_deezer_album_id_from_the_missing_set()
    {
        _albumRatings.GetAllLiked().Returns(new[]
        {
            new AlbumRating(new ArtistKey("Big Thief"), new AlbumKey("Capacity"), "art", DiscoveryStatus.Liked),
        });
        _missing.GetAll().Returns(new[]
        {
            new MissingAlbum(new ArtistKey("Big Thief"), new AlbumKey("Capacity"), "art", 12345),
        });

        var item = (await _sut.GetActive()).Single(p => p.Kind == FeedKind.MissingAlbum);

        item.DeezerAlbumId.Should().Be(12345);
    }

    [Fact]
    public async Task Retry_returns_a_failed_item_to_pending_and_it_stays_on_the_list()
    {
        _albumRatings.GetAllLiked().Returns(new[]
        {
            new AlbumRating(new ArtistKey("Big Thief"), new AlbumKey("Capacity"), "art", DiscoveryStatus.Liked),
        });
        await _sut.Reconcile();
        var id = PurchaseKey.ForAlbum("Big Thief", "Capacity");
        await _purchases.SetStatus(id, PurchaseStatus.Failed);

        // Failed items remain visible on the active list (so they can be retried), not dropped.
        (await _sut.GetActive()).Single(p => p.Id == id).Status.Should().Be(PurchaseStatus.Failed);

        (await _sut.Retry(id)).Should().BeTrue();
        (await _sut.GetActive()).Single(p => p.Id == id).Status.Should().Be(PurchaseStatus.Pending);
    }

}
