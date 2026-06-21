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

public class ArtistMetaEnricherTests
{
    private readonly IDeezerApi _deezer = Substitute.For<IDeezerApi>();
    private readonly ArtistMetaEnricher _sut;

    public ArtistMetaEnricherTests()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var resolver = new DeezerArtistResolver(_deezer, cache, Substitute.For<IArtistCatalogRepo>());
        _sut = new ArtistMetaEnricher(resolver, NullLogger<ArtistMetaEnricher>.Instance);
    }

    private static UnifiedRelatedArtist Rel(string name, string? image, params string[] sources) =>
        new(new ArtistKey(name), image, sources);

    [Fact]
    public async Task Fills_a_missing_image_from_deezer_regardless_of_recommending_source()
    {
        // ListenBrainz recommended Ariana Grande but carries no image; Deezer has her photo.
        _deezer.SearchArtist("Ariana Grande")
            .Returns(new DeezerArtist { id = 1, name = "Ariana Grande", picture_xl = "ari.jpg" });

        var input = new UnifiedRelations(new ArtistKey("100 gecs"), new[]
        {
            Rel("Ariana Grande", null, "listenbrainz"),
            Rel("Alice Gas", "alice.jpg", "deezer", "listenbrainz"), // already has an image
        });

        var result = await _sut.EnrichImages(input);

        result.Related.Single(r => r.ArtistKey.ArtistName == "Ariana Grande").ImageUrl.Should().Be("ari.jpg");
        // An artist that already had an image is left untouched — no redundant Deezer lookup.
        result.Related.Single(r => r.ArtistKey.ArtistName == "Alice Gas").ImageUrl.Should().Be("alice.jpg");
        await _deezer.DidNotReceive().SearchArtist("Alice Gas");
    }

    [Fact]
    public async Task Leaves_an_artist_with_no_deezer_match_imageless()
    {
        _deezer.SearchArtist("Obscure LB Artist").Returns((DeezerArtist?)null);

        var input = new UnifiedRelations(new ArtistKey("seed"), new[] { Rel("Obscure LB Artist", null, "listenbrainz") });

        var result = await _sut.EnrichImages(input);

        result.Related.Single().ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Feed_enrichment_touches_artist_items_only_not_missing_albums()
    {
        _deezer.SearchArtist("Aphex Twin")
            .Returns(new DeezerArtist { id = 2, name = "Aphex Twin", picture_xl = "aphex.jpg" });

        var items = new FeedItem[]
        {
            new(FeedKind.RecommendedArtist, new ArtistKey("Aphex Twin"), null, null, 1, new[] { "listenbrainz" }, null),
            new(FeedKind.MissingAlbum, new ArtistKey("Aphex Twin"), "Drukqs", null, 1, Array.Empty<string>(), 99),
        };

        var result = await _sut.EnrichImages(items);

        result.Single(i => i.Kind == FeedKind.RecommendedArtist).ImageUrl.Should().Be("aphex.jpg");
        // The album item keeps its (album-art) image path — never resolved by artist name.
        result.Single(i => i.Kind == FeedKind.MissingAlbum).ImageUrl.Should().BeNull();
    }
}
