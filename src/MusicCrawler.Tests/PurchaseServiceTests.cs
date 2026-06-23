using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MusicCrawler.Backend.Services.Download;
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

    private static readonly DownloaderConfig Config = new(
        Automatic: true, DownloadDir: "", RipBinary: "rip", Quality: "2", FallbackQuality: "1",
        Codec: "", BatchSize: 3, ItemDelay: TimeSpan.Zero, BatchInterval: TimeSpan.Zero,
        DownloadTimeout: TimeSpan.FromMinutes(15));

    public PurchaseServiceTests()
    {
        _sut = new PurchaseService(
            _purchases, _queue, _albumRatings, _library, _catalog, _missing, _downloader, Config,
            NullLogger<PurchaseService>.Instance);

        _queue.GetAllLiked().Returns(Array.Empty<DiscoveryCandidate>());
        _albumRatings.GetAllLiked().Returns(Array.Empty<AlbumRating>());
        _library.GetAllArtistMetadata().Returns(Array.Empty<ArtistMetadata>());
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        _missing.GetAll().Returns(Array.Empty<MissingAlbum>());
        _downloader.Name.Returns("test-backend");
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
    public async Task Pending_item_no_longer_liked_is_pruned_but_an_in_flight_one_is_kept()
    {
        var liked = new[]
        {
            new DiscoveryCandidate(new ArtistKey("Pending Band"), null, 1, Array.Empty<string>(), 1),
            new DiscoveryCandidate(new ArtistKey("Sent Band"), null, 1, Array.Empty<string>(), 1),
        };
        _queue.GetAllLiked().Returns(liked);
        await _sut.Reconcile();
        // "Sent Band" has been downloaded (in flight, awaiting the library).
        await _purchases.SetStatus(PurchaseKey.ForArtist("Sent Band"), PurchaseStatus.Sent);

        // Both un-liked now (nobody wants them via ratings any more).
        _queue.GetAllLiked().Returns(Array.Empty<DiscoveryCandidate>());
        var active = await _sut.GetActive();

        // The pending one is dropped; the in-flight one survives.
        active.Select(p => p.Artist.ArtistName).Should().Equal("Sent Band");
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
    public async Task Album_owned_under_a_typographically_different_title_closes_out_to_in_library()
    {
        const string likedTitle = "who told you to think??!!?!?!?!";
        // Plex stored the same album with a zero-width space and extra whitespace. A case-only
        // compare misses it, leaving an already-downloaded album stuck on the queue forever;
        // reconcile must match it the same canonical way the missing-album diff does.
        const string plexTitle = "Who told you to ​think??!!?!?!?!";

        _albumRatings.GetAllLiked().Returns(new[]
        {
            new AlbumRating(new ArtistKey("Milo"), new AlbumKey(likedTitle), "art", DiscoveryStatus.Liked),
        });
        await _sut.Reconcile();
        await _purchases.SetStatus(PurchaseKey.ForAlbum("Milo", likedTitle), PurchaseStatus.Sent);

        // It has since landed in Plex under the typographically-different title.
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Milo"] = new(StringComparer.OrdinalIgnoreCase) { plexTitle },
        });

        var active = await _sut.GetActive();

        active.Should().BeEmpty();
        _purchases.Items.Single().Status.Should().Be(PurchaseStatus.InLibrary);
    }

    [Fact]
    public async Task Album_owned_under_a_different_album_artist_closes_out_to_in_library()
    {
        // A collaboration surfaced/liked under "Milo", but the library files it under the duo
        // "Nostrum Grocers" (Deezer's album-artist, carried on the missing record). Reconcile must
        // match ownership under the album-artist, not the display artist, and close the row out.
        _albumRatings.GetAllLiked().Returns(new[]
        {
            new AlbumRating(new ArtistKey("Milo"), new AlbumKey("Nostrum Grocers"), "art", DiscoveryStatus.Liked),
        });
        _missing.GetAll().Returns(new[]
        {
            new MissingAlbum(new ArtistKey("Milo"), new AlbumKey("Nostrum Grocers"), "art", 456880775,
                new ArtistKey("Nostrum Grocers")),
        });
        await _sut.Reconcile();
        await _purchases.SetStatus(PurchaseKey.ForAlbum("Milo", "Nostrum Grocers"), PurchaseStatus.Sent);

        // It has since landed in Plex, filed under the album-artist "Nostrum Grocers" — not "Milo".
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Nostrum Grocers"] = new(StringComparer.OrdinalIgnoreCase) { "Nostrum Grocers" },
        });

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
    public async Task Failed_items_stay_on_the_active_list_for_retry()
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
    }

    [Fact]
    public async Task Snapshot_reports_backend_and_counts_by_stage()
    {
        _albumRatings.GetAllLiked().Returns(new[]
        {
            new AlbumRating(new ArtistKey("A"), new AlbumKey("queued"), null, DiscoveryStatus.Liked),
            new AlbumRating(new ArtistKey("B"), new AlbumKey("sent"), null, DiscoveryStatus.Liked),
        });
        _missing.GetAll().Returns(new[]
        {
            new MissingAlbum(new ArtistKey("A"), new AlbumKey("queued"), null, 11),
            new MissingAlbum(new ArtistKey("B"), new AlbumKey("sent"), null, 22),
        });
        await _sut.Reconcile();
        await _purchases.SetStatus(PurchaseKey.ForAlbum("B", "sent"), PurchaseStatus.Sent);

        var snap = await _sut.GetDownloadSnapshot();

        snap.Automatic.Should().BeTrue();
        snap.Backend.Should().Be(_downloader.Name);
        snap.Queued.Should().Be(1); // only the downloadable pending album with a Deezer id
        snap.Ordered.Should().Be(1);
        snap.BatchSize.Should().Be(3);
    }
}
