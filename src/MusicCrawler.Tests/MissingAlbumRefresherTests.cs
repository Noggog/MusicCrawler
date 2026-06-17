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

public class MissingAlbumRefresherTests
{
    private const string Artist = "milo";
    private const long DeezerId = 42;

    private readonly IArtistCatalogRepo _catalog = Substitute.For<IArtistCatalogRepo>();
    private readonly IDeezerApi _deezer = Substitute.For<IDeezerApi>();
    private readonly IMissingAlbumRepo _missing = Substitute.For<IMissingAlbumRepo>();
    private readonly MissingAlbumRefresher _sut;

    public MissingAlbumRefresherTests()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var resolver = new DeezerArtistResolver(_deezer, cache);
        _sut = new MissingAlbumRefresher(_catalog, resolver, _deezer, _missing, NullLogger<MissingAlbumRefresher>.Instance);

        _catalog.GetAllPresent().Returns(new[] { new CatalogArtist(new ArtistKey(Artist), null, default) });
        _deezer.SearchArtist(Artist).Returns(new DeezerArtist { id = DeezerId, name = Artist });
    }

    private static DeezerAlbum Album(string title) =>
        new() { id = 1, title = title, record_type = "album" };

    private IReadOnlyList<MissingAlbum> CapturedMissing()
    {
        var call = _missing.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IMissingAlbumRepo.ReplaceForArtist));
        return (IReadOnlyList<MissingAlbum>)call.GetArguments()[1]!;
    }

    [Fact]
    public async Task Owned_album_matches_despite_typographic_apostrophe_and_casing()
    {
        // Plex stored the title with a typographic apostrophe + title casing; Deezer returns a straight
        // apostrophe, all lower-case. Same album — it must not be reported as missing.
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Artist] = new(StringComparer.OrdinalIgnoreCase) { "So the Flies Don’t Come" },
        });
        _deezer.GetAlbums(DeezerId).Returns(new[] { Album("so the flies don't come") });

        await _sut.Refresh();

        CapturedMissing().Should().BeEmpty();
    }

    [Fact]
    public async Task Genuinely_absent_album_is_still_reported_missing()
    {
        _catalog.GetOwnedAlbums().Returns(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Artist] = new(StringComparer.OrdinalIgnoreCase) { "So the Flies Don’t Come" },
        });
        _deezer.GetAlbums(DeezerId).Returns(new[]
        {
            Album("so the flies don't come"), // owned -> skipped
            Album("budding ornithologists are weary of tired analogies"), // not owned -> missing
        });

        await _sut.Refresh();

        CapturedMissing().Select(m => m.Album.AlbumName)
            .Should().Equal("budding ornithologists are weary of tired analogies");
    }
}
