using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class DiscoveryEngineTests
{
    private const string User = "user-1";

    private readonly IUserQueueRepo _queue = Substitute.For<IUserQueueRepo>();
    private readonly IRelatedArtistReader _related = Substitute.For<IRelatedArtistReader>();
    private readonly ILibraryProvider _library = Substitute.For<ILibraryProvider>();
    private readonly IArtistCatalogRepo _catalog = Substitute.For<IArtistCatalogRepo>();
    private readonly IMissingAlbumRepo _missing = Substitute.For<IMissingAlbumRepo>();
    private readonly IUserAlbumRatingRepo _albumRatings = Substitute.For<IUserAlbumRatingRepo>();
    private readonly DiscoveryEngine _sut;

    public DiscoveryEngineTests()
    {
        _sut = new DiscoveryEngine(
            _queue, _related, _library, _catalog, _missing, _albumRatings, NullLogger<DiscoveryEngine>.Instance);

        // Sensible empty defaults; individual tests override what they need.
        _queue.GetLikedArtistNames(User).Returns(Array.Empty<string>());
        _library.GetAllArtistMetadata().Returns(Array.Empty<ArtistMetadata>());
        _queue.GetDecidedArtists(User).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _queue.CountPending(User).Returns(0);
        _queue.GetPending(User, Arg.Any<int>(), Arg.Any<int>())
            .Returns(new DiscoveryPage(Array.Empty<DiscoveryCandidate>(), 0, 20, 0));
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        _missing.GetAll().Returns(Array.Empty<MissingAlbum>());
        _albumRatings.GetDecidedKeys(User).Returns(new HashSet<string>());
    }

    private void Relates(string artist, params (string name, string? image, int sources)[] related)
    {
        var list = related
            .Select(r => new UnifiedRelatedArtist(
                new ArtistKey(r.name), r.image, Enumerable.Repeat("deezer", r.sources).ToArray()))
            .ToArray();
        _related.GetRelated(new ArtistKey(artist)).Returns(new UnifiedRelations(new ArtistKey(artist), list));
    }

    private static IReadOnlyList<DiscoveryCandidate> Captured(IUserQueueRepo queue)
    {
        var calls = queue.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IUserQueueRepo.UpsertCandidates))
            .ToList();
        calls.Should().NotBeEmpty("the engine should have upserted candidates");
        return (IReadOnlyList<DiscoveryCandidate>)calls.Last().GetArguments()[1]!;
    }

    private Task<DiscoveryFeedPage> Recommended() => _sut.GetFeed(User, FeedKind.RecommendedArtist, 0, 20);

    [Fact]
    public async Task Empty_queue_builds_from_liked_artists_one_step_out()
    {
        _queue.GetLikedArtistNames(User).Returns(new[] { "boygenius" });
        Relates("boygenius", ("Phoebe Bridgers", "pb-img", 1), ("Snail Mail", null, 1));

        await Recommended();

        var upserted = Captured(_queue);
        upserted.Select(c => c.Artist.ArtistName).Should().BeEquivalentTo("Phoebe Bridgers", "Snail Mail");
        upserted.Single(c => c.Artist.ArtistName == "Phoebe Bridgers").Sources.Should().Equal("boygenius");
    }

    [Fact]
    public async Task Existing_queue_is_not_rebuilt()
    {
        _queue.CountPending(User).Returns(3);

        await Recommended();

        await _related.DidNotReceive().GetRelated(Arg.Any<ArtistKey>());
        await _queue.DidNotReceive().UpsertCandidates(Arg.Any<string>(), Arg.Any<IReadOnlyList<DiscoveryCandidate>>());
    }

    [Fact]
    public async Task Expansion_excludes_library_and_decided_artists()
    {
        _queue.GetLikedArtistNames(User).Returns(new[] { "boygenius" });
        _library.GetAllArtistMetadata().Returns(new[] { new ArtistMetadata(new ArtistKey("Big Thief"), null) });
        _queue.GetDecidedArtists(User).Returns(new HashSet<string>(new[] { "Alex G" }, StringComparer.OrdinalIgnoreCase));
        Relates("boygenius",
            ("Big Thief", null, 1),       // already in library -> excluded
            ("Alex G", null, 1),          // already decided -> excluded
            ("boygenius", null, 1),       // the frontier artist itself -> excluded
            ("Phoebe Bridgers", null, 1)); // the one genuinely new candidate

        await Recommended();

        Captured(_queue).Select(c => c.Artist.ArtistName).Should().Equal("Phoebe Bridgers");
    }

    [Fact]
    public async Task Candidate_recommended_by_multiple_liked_artists_accrues_score_and_provenance()
    {
        _queue.GetLikedArtistNames(User).Returns(new[] { "boygenius", "Snail Mail" });
        Relates("boygenius", ("Phoebe Bridgers", "img", 1));
        Relates("Snail Mail", ("Phoebe Bridgers", null, 1));

        await Recommended();

        var pb = Captured(_queue).Single(c => c.Artist.ArtistName == "Phoebe Bridgers");
        pb.Sources.Should().BeEquivalentTo("boygenius", "Snail Mail");
        pb.Score.Should().BeGreaterThan(2.0); // two frontier artists, each ≥1 point
        pb.ImageUrl.Should().Be("img");        // image carried from whichever sighting had one
    }

    [Fact]
    public async Task Liking_an_artist_records_verdict_then_grows_the_frontier_from_it()
    {
        _queue.Rate(User, "Phoebe Bridgers", DiscoveryStatus.Liked, null)
            .Returns(new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), null, 3, new[] { "boygenius" }, 1));
        Relates("Phoebe Bridgers", ("Better Oblivion", null, 1));

        await _sut.RateArtist(User, "Phoebe Bridgers", DiscoveryStatus.Liked);

        await _queue.Received(1).Rate(User, "Phoebe Bridgers", DiscoveryStatus.Liked, null);
        var upserted = Captured(_queue);
        upserted.Select(c => c.Artist.ArtistName).Should().Equal("Better Oblivion");
        upserted.Single().Depth.Should().Be(2); // liked depth (1) + 1
    }

    [Fact]
    public async Task Disliking_an_artist_records_verdict_and_does_not_expand()
    {
        await _sut.RateArtist(User, "Phoebe Bridgers", DiscoveryStatus.Disliked);

        await _queue.Received(1).Rate(User, "Phoebe Bridgers", DiscoveryStatus.Disliked, null);
        await _related.DidNotReceive().GetRelated(Arg.Any<ArtistKey>());
        await _queue.DidNotReceive().UpsertCandidates(Arg.Any<string>(), Arg.Any<IReadOnlyList<DiscoveryCandidate>>());
    }

    [Fact]
    public async Task Rebuild_clears_pending_then_expands_from_liked_artists()
    {
        _queue.GetLikedArtistNames(User).Returns(new[] { "boygenius" });
        Relates("boygenius", ("Phoebe Bridgers", null, 1));

        await _sut.Rebuild(User);

        await _queue.Received(1).DeletePending(User);
        Captured(_queue).Select(c => c.Artist.ArtistName).Should().Equal("Phoebe Bridgers");
    }

    [Fact]
    public async Task Library_feed_shows_owned_artists_not_yet_rated()
    {
        _library.GetAllArtistMetadata().Returns(new[]
        {
            new ArtistMetadata(new ArtistKey("Big Thief"), "bt-img"),
            new ArtistMetadata(new ArtistKey("Alex G"), null),
        });
        _queue.GetDecidedArtists(User).Returns(new HashSet<string>(new[] { "Alex G" }, StringComparer.OrdinalIgnoreCase));

        var page = await _sut.GetFeed(User, FeedKind.LibraryArtist, 0, 20);

        page.Items.Select(i => i.Artist.ArtistName).Should().Equal("Big Thief");
        page.Items.Single().Kind.Should().Be(FeedKind.LibraryArtist);
        page.Total.Should().Be(1);
    }

    [Fact]
    public async Task Missing_album_feed_excludes_albums_the_user_already_decided()
    {
        _missing.GetAll().Returns(new[]
        {
            new MissingAlbum(new ArtistKey("Big Thief"), new AlbumKey("Dragon New Warm Mountain"), "art1", 101),
            new MissingAlbum(new ArtistKey("Big Thief"), new AlbumKey("Capacity"), "art2", 102),
        });
        _albumRatings.GetDecidedKeys(User).Returns(new HashSet<string> { AlbumRatingKey.For("Big Thief", "Capacity") });

        var page = await _sut.GetFeed(User, FeedKind.MissingAlbum, 0, 20);

        page.Items.Select(i => i.Album).Should().Equal("Dragon New Warm Mountain");
        page.Items.Single().Kind.Should().Be(FeedKind.MissingAlbum);
    }

    [Fact]
    public async Task Ratings_review_hides_albums_that_now_exist_in_the_library()
    {
        _albumRatings.GetRated(User).Returns(new[]
        {
            new AlbumRating(new ArtistKey("Big Thief"), new AlbumKey("Capacity"), "art", DiscoveryStatus.Liked),
            new AlbumRating(new ArtistKey("Big Thief"), new AlbumKey("U.F.O.F."), "art", DiscoveryStatus.Liked),
        });
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Big Thief"] = new(StringComparer.OrdinalIgnoreCase) { "Capacity" }, // now owned -> hidden
        });

        var ratings = await _sut.GetRatings(User);

        ratings.Where(r => r.Kind == FeedKind.MissingAlbum).Select(r => r.Album).Should().Equal("U.F.O.F.");
    }

    [Fact]
    public async Task Purchases_lists_liked_non_owned_artists_and_still_missing_liked_albums()
    {
        _library.GetAllArtistMetadata().Returns(new[] { new ArtistMetadata(new ArtistKey("Owned Band"), null) });
        _queue.GetLiked(User).Returns(new[]
        {
            new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), null, 3, new[] { "boygenius" }, 1),
            new DiscoveryCandidate(new ArtistKey("Owned Band"), null, 1, Array.Empty<string>(), 0), // owned -> excluded
        });
        _albumRatings.GetLiked(User).Returns(new[]
        {
            new AlbumRating(new ArtistKey("Owned Band"), new AlbumKey("New One"), "art", DiscoveryStatus.Liked),
        });
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));

        var purchases = await _sut.GetPurchases(User);

        purchases.Where(p => p.Kind == FeedKind.RecommendedArtist).Select(p => p.Artist.ArtistName)
            .Should().Equal("Phoebe Bridgers");
        purchases.Where(p => p.Kind == FeedKind.MissingAlbum).Select(p => p.Album).Should().Equal("New One");
    }
}
