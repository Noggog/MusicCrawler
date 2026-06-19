using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Deezer.Models;
using MusicCrawler.Deezer.Services;
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
    private readonly IDeezerApi _deezer = Substitute.For<IDeezerApi>();
    private readonly DiscoveryEngine _sut;

    public DiscoveryEngineTests()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var resolver = new DeezerArtistResolver(_deezer, cache, _catalog);
        var refresher = new MissingAlbumRefresher(
            _catalog, resolver, _deezer, _missing, NullLogger<MissingAlbumRefresher>.Instance);
        _sut = new DiscoveryEngine(
            _queue, _related, _library, _catalog, _missing, _albumRatings, refresher, NullLogger<DiscoveryEngine>.Instance);

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
    public async Task Snoozing_an_artist_records_a_snooze_and_does_not_expand()
    {
        await _sut.SnoozeArtist(User, "Phoebe Bridgers", TimeSpan.FromDays(7));

        // Snooze writes a future snoozeUntil and never grows the frontier (it's "not now", not "yes").
        await _queue.Received(1).Snooze(
            User, "Phoebe Bridgers", Arg.Is<DateTimeOffset>(d => d > DateTimeOffset.UtcNow), null);
        await _related.DidNotReceive().GetRelated(Arg.Any<ArtistKey>());
        await _queue.DidNotReceive().UpsertCandidates(Arg.Any<string>(), Arg.Any<IReadOnlyList<DiscoveryCandidate>>());
    }

    [Fact]
    public async Task Snoozing_an_album_records_a_snooze_on_the_album_ratings()
    {
        await _sut.SnoozeAlbum(User, "Big Thief", "Capacity", "art", TimeSpan.FromDays(30));

        await _albumRatings.Received(1).Snooze(
            User, "Big Thief", "Capacity", "art", Arg.Is<DateTimeOffset>(d => d > DateTimeOffset.UtcNow));
        // A snooze isn't a verdict — it must not record a Liked/Disliked rating.
        await _albumRatings.DidNotReceive().Rate(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DiscoveryStatus>());
    }

    [Fact]
    public async Task TopUp_expands_from_liked_artists_without_clearing_pending()
    {
        _queue.GetLikedArtistNames(User).Returns(new[] { "boygenius" });
        Relates("boygenius", ("Phoebe Bridgers", null, 1));

        await _sut.TopUp(User);

        // Additive: it grows the frontier but, unlike Rebuild, never wipes the existing pending queue.
        await _queue.DidNotReceive().DeletePending(Arg.Any<string>());
        Captured(_queue).Select(c => c.Artist.ArtistName).Should().Equal("Phoebe Bridgers");
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
    public async Task Library_sections_split_owned_artists_by_whether_a_liked_artist_recommends_them()
    {
        _library.GetAllArtistMetadata().Returns(new[]
        {
            new ArtistMetadata(new ArtistKey("Big Thief"), "bt-img"), // recommended by a liked artist
            new ArtistMetadata(new ArtistKey("Alex G"), null),        // nobody recommends -> seed
        });
        _queue.GetLikedArtistNames(User).Returns(new[] { "boygenius" });
        Relates("boygenius", ("Big Thief", null, 1));

        var recommended = await _sut.GetFeed(User, FeedKind.RecommendedLibraryArtist, 0, 20);
        var seed = await _sut.GetFeed(User, FeedKind.SeedLibraryArtist, 0, 20);

        var rec = recommended.Items.Single();
        rec.Artist.ArtistName.Should().Be("Big Thief");
        rec.Kind.Should().Be(FeedKind.RecommendedLibraryArtist);
        rec.Sources.Should().Equal("boygenius"); // provenance: who vouched for it

        seed.Items.Select(i => i.Artist.ArtistName).Should().Equal("Alex G");
        seed.Items.Single().Kind.Should().Be(FeedKind.SeedLibraryArtist);
    }

    [Fact]
    public async Task Library_sections_exclude_already_rated_owned_artists()
    {
        _library.GetAllArtistMetadata().Returns(new[]
        {
            new ArtistMetadata(new ArtistKey("Big Thief"), null),
            new ArtistMetadata(new ArtistKey("Alex G"), null),
        });
        _queue.GetDecidedArtists(User).Returns(new HashSet<string>(new[] { "Alex G" }, StringComparer.OrdinalIgnoreCase));

        var seed = await _sut.GetFeed(User, FeedKind.SeedLibraryArtist, 0, 20);

        // Alex G was rated, so it's gone; Big Thief has no recommender, so it lands in the seed section.
        seed.Items.Select(i => i.Artist.ArtistName).Should().Equal("Big Thief");
    }

    [Fact]
    public async Task Missing_album_feed_excludes_albums_the_user_already_decided()
    {
        _queue.GetLikedArtistNames(User).Returns(new[] { "Big Thief" });
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
    public async Task Missing_album_feed_only_surfaces_albums_from_liked_artists()
    {
        // A fresh user with no thumbs-up sees no missing albums, even though the global store has gaps
        // for every owned artist; once they like an artist, only that artist's gaps appear.
        _missing.GetAll().Returns(new[]
        {
            new MissingAlbum(new ArtistKey("Big Thief"), new AlbumKey("Capacity"), "art1", 101),
            new MissingAlbum(new ArtistKey("Wilco"), new AlbumKey("Yankee Hotel Foxtrot"), "art2", 102),
        });

        _queue.GetLikedArtistNames(User).Returns(Array.Empty<string>());
        var fresh = await _sut.GetFeed(User, FeedKind.MissingAlbum, 0, 20);
        fresh.Items.Should().BeEmpty();

        _queue.GetLikedArtistNames(User).Returns(new[] { "Big Thief" });
        var afterLike = await _sut.GetFeed(User, FeedKind.MissingAlbum, 0, 20);
        afterLike.Items.Select(i => i.Album).Should().Equal("Capacity");
    }

    [Fact]
    public async Task ArtistAlbums_surfaces_a_new_artists_discography_excluding_decided()
    {
        // Liking a brand-new artist pulls their Deezer discography as ratable missing-album items,
        // each carrying the Deezer id so a thumbs-up can flow to the downloader.
        _deezer.SearchArtist("Phoebe Bridgers").Returns(new DeezerArtist { id = 7, name = "Phoebe Bridgers" });
        _deezer.GetAlbums(7).Returns(new[]
        {
            new DeezerAlbum { id = 201, title = "Stranger in the Alps", record_type = "album" },
            new DeezerAlbum { id = 202, title = "Punisher", record_type = "album" },
        });
        _albumRatings.GetDecidedKeys(User)
            .Returns(new HashSet<string> { AlbumRatingKey.For("Phoebe Bridgers", "Punisher") });

        var items = await _sut.ArtistAlbums(User, "Phoebe Bridgers");

        items.Select(i => i.Album).Should().Equal("Stranger in the Alps");
        items.Single().Kind.Should().Be(FeedKind.MissingAlbum);
        items.Single().DeezerAlbumId.Should().Be(201);
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

}
