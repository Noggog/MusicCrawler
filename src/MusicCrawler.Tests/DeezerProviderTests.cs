using FluentAssertions;
using MusicCrawler.Deezer.Models;
using MusicCrawler.Deezer.Services;
using MusicCrawler.Interfaces;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class DeezerProviderTests
{
    private readonly IDeezerApi _deezer = Substitute.For<IDeezerApi>();
    private readonly DeezerProvider _sut;

    public DeezerProviderTests()
    {
        _sut = new DeezerProvider(_deezer);
    }

    private static DeezerArtist Artist(long id, string name) => new() { id = id, name = name };

    [Fact]
    public async Task Single_seed_maps_each_related_artist_to_a_recommendation_sourced_from_the_seed()
    {
        _deezer.SearchArtist("Radiohead").Returns(Artist(399, "Radiohead"));
        _deezer.GetRelated(399).Returns(new[] { Artist(1, "Beach House"), Artist(2, "Björk") });

        var result = await _sut.RecommendArtistsFrom(new ArtistKey("Radiohead"));

        result.Select(r => r.ArtistKey.ArtistName).Should().BeEquivalentTo("Beach House", "Björk");
        result.Should().AllSatisfy(r =>
            r.SourceArtists.Should().Equal(new ArtistKey("Radiohead")));
    }

    [Fact]
    public async Task Artist_recommended_by_two_seeds_collapses_to_one_recommendation_with_both_sources()
    {
        var a = new ArtistKey("A");
        var b = new ArtistKey("B");
        _deezer.SearchArtist("A").Returns(Artist(10, "A"));
        _deezer.SearchArtist("B").Returns(Artist(20, "B"));
        _deezer.GetRelated(10).Returns(new[] { Artist(1, "X"), Artist(2, "Shared") });
        _deezer.GetRelated(20).Returns(new[] { Artist(2, "Shared"), Artist(3, "Z") });

        var result = await _sut.RecommendArtistsFrom(new[] { a, b });

        var shared = result.Single(r => r.ArtistKey.ArtistName == "Shared");
        shared.SourceArtists.Should().BeEquivalentTo(new[] { a, b });
        result.Single(r => r.ArtistKey.ArtistName == "X").SourceArtists.Should().Equal(a);
        result.Single(r => r.ArtistKey.ArtistName == "Z").SourceArtists.Should().Equal(b);
    }

    [Fact]
    public async Task Seed_with_no_Deezer_match_is_skipped()
    {
        _deezer.SearchArtist("Unknown").Returns((DeezerArtist?)null);

        var result = await _sut.RecommendArtistsFrom(new ArtistKey("Unknown"));

        result.Should().BeEmpty();
        await _deezer.DidNotReceive().GetRelated(Arg.Any<long>());
    }

    [Fact]
    public async Task Blank_related_names_are_ignored()
    {
        _deezer.SearchArtist("Radiohead").Returns(Artist(399, "Radiohead"));
        _deezer.GetRelated(399).Returns(new[] { Artist(1, "Beach House"), Artist(2, "  ") });

        var result = await _sut.RecommendArtistsFrom(new ArtistKey("Radiohead"));

        result.Select(r => r.ArtistKey.ArtistName).Should().Equal("Beach House");
    }
}
