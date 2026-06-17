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
    private readonly IUserSeedRepo _seeds = Substitute.For<IUserSeedRepo>();
    private readonly IRelatedArtistReader _related = Substitute.For<IRelatedArtistReader>();
    private readonly ILibraryProvider _library = Substitute.For<ILibraryProvider>();
    private readonly DiscoveryEngine _sut;

    public DiscoveryEngineTests()
    {
        _sut = new DiscoveryEngine(_queue, _seeds, _related, _library, NullLogger<DiscoveryEngine>.Instance);

        // Sensible empty defaults; individual tests override what they need.
        _seeds.GetSeeds(User).Returns(Array.Empty<string>());
        _library.GetAllArtistMetadata().Returns(Array.Empty<ArtistMetadata>());
        _queue.GetDecidedArtists(User).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _queue.CountPending(User).Returns(0);
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

    [Fact]
    public async Task Empty_queue_builds_from_seeds_one_step_out()
    {
        _seeds.GetSeeds(User).Returns(new[] { "boygenius" });
        Relates("boygenius", ("Phoebe Bridgers", "pb-img", 1), ("Snail Mail", null, 1));

        await _sut.GetQueue(User, 0, 20);

        var upserted = Captured(_queue);
        upserted.Select(c => c.Artist.ArtistName).Should().BeEquivalentTo("Phoebe Bridgers", "Snail Mail");
        upserted.Single(c => c.Artist.ArtistName == "Phoebe Bridgers").Sources.Should().Equal("boygenius");
    }

    [Fact]
    public async Task Existing_queue_is_not_rebuilt()
    {
        _queue.CountPending(User).Returns(3);

        await _sut.GetQueue(User, 0, 20);

        await _related.DidNotReceive().GetRelated(Arg.Any<ArtistKey>());
        await _queue.DidNotReceive().UpsertCandidates(Arg.Any<string>(), Arg.Any<IReadOnlyList<DiscoveryCandidate>>());
    }

    [Fact]
    public async Task Expansion_excludes_library_seeds_and_decided_artists()
    {
        _seeds.GetSeeds(User).Returns(new[] { "boygenius" });
        _library.GetAllArtistMetadata().Returns(new[] { new ArtistMetadata(new ArtistKey("Big Thief"), null) });
        _queue.GetDecidedArtists(User).Returns(new HashSet<string>(new[] { "Alex G" }, StringComparer.OrdinalIgnoreCase));
        Relates("boygenius",
            ("Big Thief", null, 1),      // already in library -> excluded
            ("Alex G", null, 1),          // already decided -> excluded
            ("boygenius", null, 1),       // the frontier artist itself -> excluded
            ("Phoebe Bridgers", null, 1)); // the one genuinely new candidate

        await _sut.GetQueue(User, 0, 20);

        Captured(_queue).Select(c => c.Artist.ArtistName).Should().Equal("Phoebe Bridgers");
    }

    [Fact]
    public async Task Candidate_recommended_by_multiple_seeds_accrues_score_and_provenance()
    {
        _seeds.GetSeeds(User).Returns(new[] { "boygenius", "Snail Mail" });
        Relates("boygenius", ("Phoebe Bridgers", "img", 1));
        Relates("Snail Mail", ("Phoebe Bridgers", null, 1));

        await _sut.GetQueue(User, 0, 20);

        var pb = Captured(_queue).Single(c => c.Artist.ArtistName == "Phoebe Bridgers");
        pb.Sources.Should().BeEquivalentTo("boygenius", "Snail Mail");
        pb.Score.Should().BeGreaterThan(2.0); // two frontier artists, each ≥1 point
        pb.ImageUrl.Should().Be("img");        // image carried from whichever sighting had one
    }

    [Fact]
    public async Task Like_records_verdict_then_grows_the_frontier_from_the_liked_artist()
    {
        _queue.SetVerdict(User, "Phoebe Bridgers", DiscoveryStatus.Liked)
            .Returns(new DiscoveryCandidate(new ArtistKey("Phoebe Bridgers"), null, 3, new[] { "boygenius" }, 1));
        Relates("Phoebe Bridgers", ("Better Oblivion", null, 1));

        await _sut.Like(User, "Phoebe Bridgers");

        await _queue.Received(1).SetVerdict(User, "Phoebe Bridgers", DiscoveryStatus.Liked);
        var upserted = Captured(_queue);
        upserted.Select(c => c.Artist.ArtistName).Should().Equal("Better Oblivion");
        upserted.Single().Depth.Should().Be(2); // liked depth (1) + 1
    }

    [Fact]
    public async Task Dislike_records_verdict_and_does_not_expand()
    {
        await _sut.Dislike(User, "Phoebe Bridgers");

        await _queue.Received(1).SetVerdict(User, "Phoebe Bridgers", DiscoveryStatus.Disliked);
        await _related.DidNotReceive().GetRelated(Arg.Any<ArtistKey>());
        await _queue.DidNotReceive().UpsertCandidates(Arg.Any<string>(), Arg.Any<IReadOnlyList<DiscoveryCandidate>>());
    }

    [Fact]
    public async Task Rebuild_clears_pending_then_expands_from_seeds()
    {
        _seeds.GetSeeds(User).Returns(new[] { "boygenius" });
        Relates("boygenius", ("Phoebe Bridgers", null, 1));

        await _sut.Rebuild(User, 0, 20);

        await _queue.Received(1).DeletePending(User);
        Captured(_queue).Select(c => c.Artist.ArtistName).Should().Equal("Phoebe Bridgers");
    }
}
