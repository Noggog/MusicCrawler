using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Mycelium.Backend;
using Mycelium.Backend.Services.Background;
using Mycelium.Backend.Services.Singletons;
using Mycelium.Interfaces;
using Mycelium.Plex.Services.Singletons;
using NSubstitute;
using Xunit;

namespace Mycelium.Tests;

/// <summary>
/// The weekly pass that decides which thumbed-down artists the user's own Plex song ratings contradict.
/// All the threshold judgement lives here (the feed just serves what this flags), so this is where the
/// "3+ stars across at least a third of the songs" rule is pinned down.
/// </summary>
public class ReconsiderSweepServiceTests
{
    private const string User = "user-1";

    private readonly IUserQueueRepo _queue = Substitute.For<IUserQueueRepo>();
    private readonly ILibraryProvider _library = Substitute.For<ILibraryProvider>();
    private readonly IArtistCatalogRepo _catalog = Substitute.For<IArtistCatalogRepo>();
    private readonly IPlexApi _plex = Substitute.For<IPlexApi>();

    public ReconsiderSweepServiceTests()
    {
        _queue.GetAllUserIds().Returns(new[] { User });
        _queue.GetUnconfirmedDislikes(Arg.Any<string>()).Returns(Array.Empty<DislikedArtist>());
        _library.GetAllArtistMetadata().Returns(Array.Empty<ArtistMetadata>());
        _catalog.GetPlexRatingKeys(Arg.Any<ArtistKey>()).Returns(Array.Empty<int>());
    }

    private ReconsiderSweepService Build() => new(
        _queue,
        _library,
        new ArtistRatingStatsService(_catalog, _plex, NullLogger<ArtistRatingStatsService>.Instance),
        // The shipped thresholds; cadence is irrelevant when driving SweepAll directly.
        new ReconsiderPolicy(
            MinAverage: 3, MinRatedFraction: 1.0 / 3,
            Interval: TimeSpan.FromDays(7), StartupDelay: TimeSpan.Zero),
        new JitterPolicy(0),
        NullLogger<ReconsiderSweepService>.Instance);

    /// <summary>
    /// Stubs a thumbed-down artist: owned by the library, with the given per-song Plex ratings (Plex's
    /// 0–10 scale, halved to stars downstream). <paramref name="alreadyFlagged"/> is the verdict the row
    /// currently carries, so tests can assert the sweep only writes on a change.
    /// </summary>
    private void Disliked(
        string artist, int ratingKey, double?[] plexRatings, ReconsiderSignal? alreadyFlagged = null,
        string? users = null, bool owned = true)
    {
        var userId = users ?? User;
        var existing = _queue.GetUnconfirmedDislikes(userId).Result;
        _queue.GetUnconfirmedDislikes(userId).Returns(existing
            .Append(new DislikedArtist(new ArtistKey(artist), null, alreadyFlagged))
            .ToArray());

        if (owned)
        {
            var library = _library.GetAllArtistMetadata().Result;
            _library.GetAllArtistMetadata().Returns(library
                .Append(new ArtistMetadata(new ArtistKey(artist), $"{artist}-img"))
                .ToArray());
        }

        _catalog.GetPlexRatingKeys(new ArtistKey(artist)).Returns(new[] { ratingKey });
        _plex.GetArtistTracks(ratingKey).Returns(
            plexRatings.Select(r => new PlexTrack { Title = "t", UserRating = r }).ToArray());
    }

    [Fact]
    public async Task Flags_a_dislike_the_song_ratings_contradict()
    {
        // 4 of 6 songs rated (past the 1/3 bar), averaging 4 stars (past the 3-star bar) — the
        // thumbs-down looks like the mistake. The catalog art is stamped on while we're here, so the
        // feed can serve the card from this one row.
        Disliked("Low", 10, new double?[] { 10, 8, 8, 6, null, null });

        await Build().SweepAll();

        await _queue.Received(1).SetReconsider(User, "Low", new ReconsiderSignal(4.0, 4, 6), "Low-img");
    }

    [Fact]
    public async Task Skips_a_high_average_over_too_few_rated_songs()
    {
        // Two 5-star songs out of twenty is a great average on almost no evidence — under the 1/3 bar.
        var ratings = new double?[20];
        ratings[0] = 10;
        ratings[1] = 10;
        Disliked("Sunn O)))", 10, ratings);

        await Build().SweepAll();

        await _queue.DidNotReceive().SetReconsider(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReconsiderSignal>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Skips_a_well_rated_but_low_scoring_artist()
    {
        // Plenty rated, but they averaged 2 stars — the dislike is exactly what the ratings say.
        Disliked("Nickelback", 10, new double?[] { 4, 4, 4, 4 });

        await Build().SweepAll();

        await _queue.DidNotReceive().SetReconsider(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReconsiderSignal>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Skips_an_artist_with_nothing_rated()
    {
        // No stars at all carries no signal either way, so it can't contradict the thumbs-down.
        Disliked("Ministry", 10, new double?[] { null, null, null });

        await Build().SweepAll();

        await _queue.DidNotReceive().SetReconsider(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReconsiderSignal>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Ignores_dislikes_for_artists_the_library_doesnt_own()
    {
        // A rejected recommendation has no songs in Plex, so it can never qualify — and we shouldn't
        // ask Plex about it at all.
        Disliked("Not Owned", 10, new double?[] { 10, 10, 10 }, owned: false);

        await Build().SweepAll();

        await _plex.DidNotReceive().GetArtistTracks(Arg.Any<int>());
        await _queue.DidNotReceive().SetReconsider(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReconsiderSignal>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Withdraws_a_flag_the_ratings_no_longer_support()
    {
        // Flagged on an earlier pass; since then the user rated more of the songs down, so the verdict
        // has to be taken back rather than left to rot.
        Disliked("Low", 10, new double?[] { 4, 4, 4, 4 }, alreadyFlagged: new ReconsiderSignal(4.0, 4, 6));

        await Build().SweepAll();

        await _queue.Received(1).SetReconsider(User, "Low", null, "Low-img");
    }

    [Fact]
    public async Task Rewrites_a_flag_whose_numbers_drifted()
    {
        // Still qualifies, but the user has rated two more songs — refresh the stored evidence so the
        // card doesn't quote stale numbers.
        Disliked("Low", 10, new double?[] { 10, 8, 8, 6, 8, 8 }, alreadyFlagged: new ReconsiderSignal(4.0, 4, 6));

        await Build().SweepAll();

        await _queue.Received(1).SetReconsider(
            User, "Low", new ReconsiderSignal(4.0, 6, 6), "Low-img");
    }

    [Fact]
    public async Task Writes_nothing_when_an_existing_flag_still_holds()
    {
        // The steady state — the same artists stay flagged week after week, and the pass should be a
        // pure read rather than churning writes for no reason.
        Disliked("Low", 10, new double?[] { 10, 8, 8, 6, null, null }, alreadyFlagged: new ReconsiderSignal(4.0, 4, 6));

        await Build().SweepAll();

        await _queue.DidNotReceive().SetReconsider(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReconsiderSignal?>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Resolves_each_artist_once_across_users()
    {
        // Plex ratings hang off the single shared server token, not the app user, so two users who both
        // rejected the same band see identical numbers — one Plex pull should cover both.
        _queue.GetAllUserIds().Returns(new[] { "u1", "u2" });
        Disliked("Low", 10, new double?[] { 10, 8, 8, 6 }, users: "u1");
        Disliked("Low", 10, new double?[] { 10, 8, 8, 6 }, users: "u2");

        await Build().SweepAll();

        await _plex.Received(1).GetArtistTracks(10);
        await _queue.Received(1).SetReconsider("u1", "Low", Arg.Any<ReconsiderSignal>(), Arg.Any<string>());
        await _queue.Received(1).SetReconsider("u2", "Low", Arg.Any<ReconsiderSignal>(), Arg.Any<string>());
    }

    [Fact]
    public async Task One_failing_user_does_not_stop_the_rest()
    {
        _queue.GetAllUserIds().Returns(new[] { "u1", "u2" });
        _queue.GetUnconfirmedDislikes("u1")
            .Returns<DislikedArtist[]>(_ => throw new InvalidOperationException("boom"));
        Disliked("Low", 10, new double?[] { 10, 8, 8, 6 }, users: "u2");

        await Build().SweepAll();

        await _queue.Received(1).SetReconsider("u2", "Low", Arg.Any<ReconsiderSignal>(), Arg.Any<string>());
    }

    [Fact]
    public async Task A_failure_enumerating_users_ends_the_pass_quietly()
    {
        _queue.GetAllUserIds().Returns<string[]>(_ => throw new InvalidOperationException("mongo down"));

        // No throw — the pass just retries at the next interval rather than crashing the host.
        await Build().SweepAll();

        await _queue.DidNotReceive().GetUnconfirmedDislikes(Arg.Any<string>());
    }
}
