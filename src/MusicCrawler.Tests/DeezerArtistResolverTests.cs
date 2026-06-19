using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Deezer.Models;
using MusicCrawler.Deezer.Services;
using MusicCrawler.Interfaces;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class DeezerArtistResolverTests
{
    private readonly IDeezerApi _deezer = Substitute.For<IDeezerApi>();
    private readonly IArtistCatalogRepo _catalog = Substitute.For<IArtistCatalogRepo>();
    private readonly IDistributedCache _cache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly DeezerArtistResolver _sut;

    private static readonly ArtistKey Alex = new("ALEX");

    public DeezerArtistResolverTests()
    {
        _sut = new DeezerArtistResolver(_deezer, _cache, _catalog);
    }

    [Fact]
    public async Task Override_resolves_by_pinned_id_and_never_name_searches()
    {
        // The synthwave ALEX the user pinned — not the pop "Alex Warren" a name search would pick.
        var pinned = new DeezerIdentity(72639412, "ALEX", 1524, "link", "img");
        _catalog.GetDeezer(Alex).Returns((pinned, true));

        var result = await _sut.ResolveIdentity("ALEX");

        result.Should().Be(pinned);
        await _deezer.DidNotReceive().SearchArtist(Arg.Any<string>());
    }

    [Fact]
    public async Task No_override_falls_back_to_name_search_and_captures_the_id()
    {
        _catalog.GetDeezer(Alex).Returns(((DeezerIdentity, bool)?)null);
        _deezer.SearchArtist("ALEX").Returns(new DeezerArtist { id = 541784, name = "Alex Warren", nb_fan = 146474 });

        var result = await _sut.ResolveIdentity("ALEX");

        result!.Id.Should().Be(541784);
        // Opportunistic capture onto the catalog (isOverride: false).
        await _catalog.Received(1).SetDeezerIdentity(Alex,
            Arg.Is<DeezerIdentity>(i => i.Id == 541784), false);
    }

    [Fact]
    public async Task Cache_hit_still_captures_the_id_onto_an_empty_catalog()
    {
        // A warm (persistent/Redis) cache must not stop the catalog from being populated — the bug
        // that left every artist "unresolved" after a resolve-all despite cache hits.
        _catalog.GetDeezer(Alex).Returns(((DeezerIdentity, bool)?)null);
        var key = "deezer:artist:v2:alex";
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(
            new DeezerIdentity(541784, "Alex Warren", 146474, "link", "img")));

        var result = await _sut.ResolveIdentity("ALEX");

        result!.Id.Should().Be(541784);
        await _deezer.DidNotReceive().SearchArtist(Arg.Any<string>()); // served from cache
        await _catalog.Received(1).SetDeezerIdentity(Alex,
            Arg.Is<DeezerIdentity>(i => i.Id == 541784), false); // but still persisted
    }

    [Fact]
    public async Task Cache_hit_skips_the_write_when_catalog_already_has_that_id()
    {
        var identity = new DeezerIdentity(541784, "Alex Warren", 146474, "link", "img");
        _catalog.GetDeezer(Alex).Returns((identity, false));
        await _cache.SetStringAsync("deezer:artist:v2:alex", JsonSerializer.Serialize(identity));

        await _sut.ResolveIdentity("ALEX");

        await _catalog.DidNotReceive().SetDeezerIdentity(Arg.Any<ArtistKey>(), Arg.Any<DeezerIdentity>(), false);
    }

    [Fact]
    public async Task SetOverride_fetches_by_id_and_persists_a_sticky_pin()
    {
        _deezer.GetArtist(72639412).Returns(new DeezerArtist { id = 72639412, name = "ALEX", nb_fan = 1524 });

        var result = await _sut.SetOverride("ALEX", 72639412);

        result!.Id.Should().Be(72639412);
        await _catalog.Received(1).SetDeezerIdentity(Alex,
            Arg.Is<DeezerIdentity>(i => i.Id == 72639412), true);
    }

    [Fact]
    public async Task SetOverride_returns_null_when_the_id_has_no_Deezer_artist()
    {
        _deezer.GetArtist(999).Returns((DeezerArtist?)null);

        var result = await _sut.SetOverride("ALEX", 999);

        result.Should().BeNull();
        await _catalog.DidNotReceive().SetDeezerIdentity(Arg.Any<ArtistKey>(), Arg.Any<DeezerIdentity>(), true);
    }
}
