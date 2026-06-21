using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using MusicCrawler.ListenBrainz.Models;
using MusicCrawler.ListenBrainz.Services;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class MusicBrainzArtistResolverTests
{
    private readonly IMusicBrainzApi _musicBrainz = Substitute.For<IMusicBrainzApi>();
    private readonly IArtistCatalogRepo _catalog = Substitute.For<IArtistCatalogRepo>();
    private readonly IDistributedCache _cache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly MusicBrainzArtistResolver _sut;

    private static readonly ArtistKey Alex = new("ALEX");
    private const string PinnedMbid = "11111111-1111-1111-1111-111111111111";
    private const string SearchMbid = "22222222-2222-2222-2222-222222222222";

    public MusicBrainzArtistResolverTests()
    {
        _sut = new MusicBrainzArtistResolver(_musicBrainz, _cache, _catalog);
    }

    [Fact]
    public async Task Override_resolves_by_pinned_mbid_and_never_name_searches()
    {
        var pinned = new MusicBrainzIdentity(PinnedMbid, "ALEX", "synthwave project");
        _catalog.GetMusicBrainz(Alex).Returns((pinned, true));

        var result = await _sut.ResolveIdentity("ALEX");

        result.Should().Be(pinned);
        await _musicBrainz.DidNotReceive().SearchArtist(Arg.Any<string>());
    }

    [Fact]
    public async Task No_override_falls_back_to_name_search_and_captures_the_mbid()
    {
        _catalog.GetMusicBrainz(Alex).Returns(((MusicBrainzIdentity, bool)?)null);
        _musicBrainz.SearchArtist("ALEX").Returns(new MusicBrainzArtist { Id = SearchMbid, Name = "Alex Warren" });

        var result = await _sut.ResolveIdentity("ALEX");

        result!.Mbid.Should().Be(SearchMbid);
        await _catalog.Received(1).SetMusicBrainzIdentity(Alex,
            Arg.Is<MusicBrainzIdentity>(i => i.Mbid == SearchMbid), false);
    }

    [Fact]
    public async Task Cache_hit_still_captures_the_mbid_onto_an_empty_catalog()
    {
        // A warm (Redis) cache must not stop the catalog from being populated for the Sources tab.
        _catalog.GetMusicBrainz(Alex).Returns(((MusicBrainzIdentity, bool)?)null);
        // Cached value shape is "mbid\tname\tdisambiguation".
        await _cache.SetStringAsync("musicbrainz:artist:v1:alex", $"{SearchMbid}\tAlex Warren\t");

        var result = await _sut.ResolveIdentity("ALEX");

        result!.Mbid.Should().Be(SearchMbid);
        await _musicBrainz.DidNotReceive().SearchArtist(Arg.Any<string>()); // served from cache
        await _catalog.Received(1).SetMusicBrainzIdentity(Alex,
            Arg.Is<MusicBrainzIdentity>(i => i.Mbid == SearchMbid), false);
    }

    [Fact]
    public async Task Cache_hit_skips_the_write_when_catalog_already_has_that_mbid()
    {
        var identity = new MusicBrainzIdentity(SearchMbid, "Alex Warren", null);
        _catalog.GetMusicBrainz(Alex).Returns((identity, false));
        await _cache.SetStringAsync("musicbrainz:artist:v1:alex", $"{SearchMbid}\tAlex Warren\t");

        await _sut.ResolveIdentity("ALEX");

        await _catalog.DidNotReceive().SetMusicBrainzIdentity(
            Arg.Any<ArtistKey>(), Arg.Any<MusicBrainzIdentity>(), false);
    }

    [Fact]
    public async Task SetOverride_looks_up_by_mbid_and_persists_a_sticky_pin()
    {
        _musicBrainz.GetArtist(PinnedMbid).Returns(new MusicBrainzArtist { Id = PinnedMbid, Name = "ALEX" });

        var result = await _sut.SetOverride("ALEX", PinnedMbid);

        result!.Mbid.Should().Be(PinnedMbid);
        await _catalog.Received(1).SetMusicBrainzIdentity(Alex,
            Arg.Is<MusicBrainzIdentity>(i => i.Mbid == PinnedMbid), true);
    }

    [Fact]
    public async Task SetOverride_returns_null_when_the_mbid_has_no_artist()
    {
        _musicBrainz.GetArtist("nope").Returns((MusicBrainzArtist?)null);

        var result = await _sut.SetOverride("ALEX", "nope");

        result.Should().BeNull();
        await _catalog.DidNotReceive().SetMusicBrainzIdentity(
            Arg.Any<ArtistKey>(), Arg.Any<MusicBrainzIdentity>(), true);
    }
}
