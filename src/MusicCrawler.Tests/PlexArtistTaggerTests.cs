using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using MusicCrawler.Plex.Services.Singletons;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class PlexArtistTaggerTests
{
    private const string Artist = "Radiohead";
    private const string Liked = "noggog_liked";
    private const string Disliked = "noggog_disliked";

    private readonly IPlexApi _plex = Substitute.For<IPlexApi>();
    private readonly IArtistCatalogRepo _catalog = Substitute.For<IArtistCatalogRepo>();
    private readonly PlexArtistTagger _sut;

    public PlexArtistTaggerTests()
    {
        _sut = new PlexArtistTagger(_plex, _catalog, NullLogger<PlexArtistTagger>.Instance);
        _plex.ResolveLibrary().Returns(new PlexLibrary { Key = 1, Title = "Music", Type = "artist" });
        // Default: artist not in catalog (no stored keys) and an empty library — individual tests override.
        _catalog.GetPlexRatingKeys(Arg.Any<ArtistKey>()).Returns(Array.Empty<int>());
        _plex.GetMusicArtists(Arg.Any<int>()).Returns(Array.Empty<PlexMusicArtist>());
    }

    private static PlexMusicArtist ArtistItem(int ratingKey, string title, params string[] collections) =>
        new()
        {
            RatingKey = ratingKey,
            Title = title,
            Collection = collections.Select(l => new PlexTag { Tag = l }).ToArray(),
        };

    private void StoredKeys(params int[] keys) =>
        _catalog.GetPlexRatingKeys(new ArtistKey(Artist)).Returns(keys);

    // --- ReconcileCollections (pure) -----------------------------------------------------------

    [Fact]
    public void Reconcile_adds_when_absent_and_preserves_others()
    {
        PlexArtistTagger.ReconcileCollections(new[] { "rock" }, Liked, Array.Empty<string>())
            .Should().Equal("rock", Liked);
    }

    [Fact]
    public void Reconcile_removes_case_insensitively_and_preserves_others()
    {
        PlexArtistTagger.ReconcileCollections(new[] { "rock", "NOGGOG_LIKED" }, null, new[] { Liked })
            .Should().Equal("rock");
    }

    [Fact]
    public void Reconcile_flip_drops_opposite_and_adds_new()
    {
        PlexArtistTagger.ReconcileCollections(new[] { "rock", Liked }, Disliked, new[] { Liked })
            .Should().Equal("rock", Disliked);
    }

    [Fact]
    public void Reconcile_returns_null_when_already_in_desired_state()
    {
        // Add already present, nothing to remove.
        PlexArtistTagger.ReconcileCollections(new[] { "rock", Liked }, Liked, new[] { Disliked })
            .Should().BeNull();
        // Add present (case-insensitive), remove tag absent.
        PlexArtistTagger.ReconcileCollections(new[] { "NOGGOG_LIKED" }, Liked, new[] { Disliked })
            .Should().BeNull();
    }

    // --- SetTags orchestration -----------------------------------------------------------------

    [Fact]
    public async Task Stored_key_fast_path_never_scans_the_library()
    {
        StoredKeys(5);
        _plex.GetMusicArtist(5).Returns(ArtistItem(5, Artist, "rock"));

        await _sut.SetTags(Artist, Liked, Array.Empty<string>());

        await _plex.Received(1).SetArtistCollections(1, 5, Arg.Is<IReadOnlyList<string>>(
            l => l.SequenceEqual(new[] { "rock", Liked })));
        await _plex.DidNotReceive().GetMusicArtists(Arg.Any<int>());
    }

    [Fact]
    public async Task Flip_strips_opposite_and_stamps_new_verdict()
    {
        StoredKeys(5);
        _plex.GetMusicArtist(5).Returns(ArtistItem(5, Artist, Liked));

        await _sut.SetTags(Artist, Disliked, new[] { Liked });

        await _plex.Received(1).SetArtistCollections(1, 5, Arg.Is<IReadOnlyList<string>>(
            l => l.SequenceEqual(new[] { Disliked })));
    }

    [Fact]
    public async Task Clear_strips_both_verdict_tags()
    {
        StoredKeys(5);
        _plex.GetMusicArtist(5).Returns(ArtistItem(5, Artist, "rock", Liked));

        await _sut.SetTags(Artist, add: null, remove: new[] { Liked, Disliked });

        await _plex.Received(1).SetArtistCollections(1, 5, Arg.Is<IReadOnlyList<string>>(
            l => l.SequenceEqual(new[] { "rock" })));
    }

    [Fact]
    public async Task No_op_when_already_in_desired_state_writes_nothing()
    {
        StoredKeys(5);
        _plex.GetMusicArtist(5).Returns(ArtistItem(5, Artist, "rock", Liked));

        await _sut.SetTags(Artist, Liked, new[] { Disliked });

        await _plex.DidNotReceive().SetArtistCollections(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<string>>());
    }

    [Fact]
    public async Task Multi_key_collaborators_tag_every_item()
    {
        StoredKeys(5, 9);
        _plex.GetMusicArtist(5).Returns(ArtistItem(5, Artist, "rock"));
        _plex.GetMusicArtist(9).Returns(ArtistItem(9, $"{Artist};Other", "pop"));

        await _sut.SetTags(Artist, Liked, Array.Empty<string>());

        await _plex.Received(1).SetArtistCollections(1, 5, Arg.Is<IReadOnlyList<string>>(
            l => l.SequenceEqual(new[] { "rock", Liked })));
        await _plex.Received(1).SetArtistCollections(1, 9, Arg.Is<IReadOnlyList<string>>(
            l => l.SequenceEqual(new[] { "pop", Liked })));
    }

    [Fact]
    public async Task Cold_cache_falls_back_to_the_name_scan()
    {
        // No stored keys (default) → scan path.
        _plex.GetMusicArtists(1).Returns(new[] { ArtistItem(7, Artist, "rock") });

        await _sut.SetTags(Artist, Liked, Array.Empty<string>());

        await _plex.Received(1).GetMusicArtists(1);
        await _plex.Received(1).SetArtistCollections(1, 7, Arg.Is<IReadOnlyList<string>>(
            l => l.SequenceEqual(new[] { "rock", Liked })));
        await _plex.DidNotReceive().GetMusicArtist(Arg.Any<int>());
    }

    [Fact]
    public async Task Stale_key_falls_back_to_the_name_scan()
    {
        StoredKeys(5);
        _plex.GetMusicArtist(5).Returns((PlexMusicArtist?)null); // key no longer resolves
        _plex.GetMusicArtists(1).Returns(new[] { ArtistItem(7, Artist, "rock") });

        await _sut.SetTags(Artist, Liked, Array.Empty<string>());

        await _plex.Received(1).GetMusicArtists(1);
        await _plex.Received(1).SetArtistCollections(1, 7, Arg.Is<IReadOnlyList<string>>(
            l => l.SequenceEqual(new[] { "rock", Liked })));
    }

    [Fact]
    public async Task Best_effort_does_not_rethrow()
    {
        StoredKeys(5);
        _plex.GetMusicArtist(5).Returns<PlexMusicArtist?>(_ => throw new InvalidOperationException("plex down"));

        var act = async () => await _sut.SetTags(Artist, Liked, Array.Empty<string>());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Blank_input_short_circuits_without_touching_plex_or_catalog()
    {
        await _sut.SetTags("   ", add: null, remove: Array.Empty<string>());

        await _catalog.DidNotReceive().GetPlexRatingKeys(Arg.Any<ArtistKey>());
        await _plex.DidNotReceive().ResolveLibrary();
    }
}
