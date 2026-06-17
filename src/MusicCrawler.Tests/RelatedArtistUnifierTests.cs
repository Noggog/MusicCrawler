using FluentAssertions;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using Xunit;

namespace MusicCrawler.Tests;

public class RelatedArtistUnifierTests
{
    private static ArtistRelations Source(string source, params RelatedArtist[] related) =>
        new(new ArtistKey("seed"), source, related, DateTimeOffset.UtcNow);

    private static RelatedArtist Rel(string name, string? image = null) =>
        new(new ArtistKey(name), image);

    [Fact]
    public void Empty_input_yields_empty_list()
    {
        RelatedArtistUnifier.Unify(Array.Empty<ArtistRelations>()).Should().BeEmpty();
    }

    [Fact]
    public void Single_source_returns_all_artists_tagged_with_that_source()
    {
        var result = RelatedArtistUnifier.Unify(new[]
        {
            Source("deezer", Rel("Beach House", "img-bh"), Rel("Björk", "img-bj")),
        });

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.Sources.Should().Equal("deezer"));
        result.Select(r => r.ArtistKey.ArtistName).Should().Contain(new[] { "Beach House", "Björk" });
    }

    [Fact]
    public void Same_artist_from_two_sources_is_deduped_and_carries_both_sources()
    {
        var result = RelatedArtistUnifier.Unify(new[]
        {
            Source("deezer", Rel("Björk", "img")),
            Source("lastfm", Rel("Björk", "img")),
        });

        result.Should().ContainSingle()
            .Which.Sources.Should().Equal("deezer", "lastfm");
    }

    [Fact]
    public void Dedup_is_case_insensitive_and_keeps_first_display_name()
    {
        var result = RelatedArtistUnifier.Unify(new[]
        {
            Source("deezer", Rel("Björk")),
            Source("lastfm", Rel("BJÖRK")),
        });

        result.Should().ContainSingle();
        result[0].ArtistKey.ArtistName.Should().Be("Björk"); // first-encountered casing wins
        result[0].Sources.Should().Equal("deezer", "lastfm");
    }

    [Fact]
    public void Image_uses_first_non_null_across_sources()
    {
        // First source has no image; second supplies one — the unified entry should adopt it.
        var result = RelatedArtistUnifier.Unify(new[]
        {
            Source("deezer", Rel("Air", image: null)),
            Source("lastfm", Rel("Air", image: "img-air")),
        });

        result.Should().ContainSingle().Which.ImageUrl.Should().Be("img-air");
    }

    [Fact]
    public void Image_keeps_first_non_null_when_sources_disagree()
    {
        var result = RelatedArtistUnifier.Unify(new[]
        {
            Source("deezer", Rel("Air", "first")),
            Source("lastfm", Rel("Air", "second")),
        });

        result.Should().ContainSingle().Which.ImageUrl.Should().Be("first");
    }

    [Fact]
    public void Same_source_listing_an_artist_twice_does_not_duplicate_the_source_tag()
    {
        var result = RelatedArtistUnifier.Unify(new[]
        {
            Source("deezer", Rel("Air"), Rel("Air")),
        });

        result.Should().ContainSingle().Which.Sources.Should().Equal("deezer");
    }

    [Fact]
    public void Results_are_sorted_by_name_case_insensitively()
    {
        var result = RelatedArtistUnifier.Unify(new[]
        {
            Source("deezer", Rel("zebra"), Rel("Apple"), Rel("mango")),
        });

        result.Select(r => r.ArtistKey.ArtistName).Should().Equal("Apple", "mango", "zebra");
    }
}
