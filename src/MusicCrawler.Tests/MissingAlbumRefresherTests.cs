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
        var resolver = new DeezerArtistResolver(_deezer, cache, _catalog);
        _sut = new MissingAlbumRefresher(_catalog, resolver, _deezer, _missing, NullLogger<MissingAlbumRefresher>.Instance);

        _catalog.GetAllPresent().Returns(new[] { new CatalogArtist(new ArtistKey(Artist), null, default) });
        _deezer.SearchArtist(Artist).Returns(new DeezerArtist { id = DeezerId, name = Artist });
    }

    private static DeezerAlbum Album(string title, string recordType = "album", long id = 1) =>
        new() { id = id, title = title, record_type = recordType };

    private static Dictionary<string, HashSet<string>> Owned(params (string Artist, string[] Albums)[] entries)
    {
        var d = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (artist, albums) in entries)
        {
            d[artist] = new HashSet<string>(albums, StringComparer.OrdinalIgnoreCase);
        }
        return d;
    }

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

    [Fact]
    public async Task RefreshOne_with_no_owned_albums_surfaces_whole_discography_albums_only()
    {
        // The brand-new-artist path: nothing is owned, so every album-type record should surface as
        // acquirable — while singles/EPs/compilations stay filtered out.
        _deezer.GetAlbums(DeezerId).Returns(new[]
        {
            Album("first lp"),
            Album("second lp"),
            Album("a single", recordType: "single"),
            Album("an ep", recordType: "ep"),
        });

        var result = await _sut.RefreshOne(new ArtistKey(Artist), Owned());

        result.Select(m => m.Album.AlbumName).Should().BeEquivalentTo("first lp", "second lp");
        CapturedMissing().Select(m => m.Album.AlbumName).Should().BeEquivalentTo("first lp", "second lp");
    }

    [Fact]
    public async Task Collaboration_owned_under_its_album_artist_is_not_missing()
    {
        // "milo" lists a duo record on Deezer whose real album-artist is "nostrum grocers" — which is
        // how the library filed it. Even though milo's own owned set lacks it, it must NOT surface as
        // missing: it's owned under the album-artist.
        _catalog.GetOwnedAlbums().Returns(Owned(("nostrum grocers", new[] { "Nostrum Grocers" })));
        _deezer.GetAlbums(DeezerId).Returns(new[] { Album("nostrum grocers", id: 99) });
        _deezer.GetAlbum(99).Returns(new DeezerAlbum
        {
            id = 99, title = "nostrum grocers", artist = new DeezerArtist { name = "nostrum grocers" },
        });

        await _sut.Refresh();

        CapturedMissing().Should().BeEmpty();
    }

    [Fact]
    public async Task Collaboration_not_owned_is_missing_but_carries_its_album_artist()
    {
        // Same duo record, but the library doesn't have it anywhere. It surfaces as missing under the
        // listing artist (so it stays discoverable on milo's feed), yet matches ownership under the
        // album-artist the library would file it under.
        _catalog.GetOwnedAlbums().Returns(Owned());
        _deezer.GetAlbums(DeezerId).Returns(new[] { Album("nostrum grocers", id: 99) });
        _deezer.GetAlbum(99).Returns(new DeezerAlbum
        {
            id = 99, title = "nostrum grocers", artist = new DeezerArtist { name = "Nostrum Grocers" },
        });

        await _sut.Refresh();

        var m = CapturedMissing().Single();
        m.Artist.ArtistName.Should().Be(Artist);                 // surfaces under the listing artist
        m.MatchArtist.ArtistName.Should().Be("Nostrum Grocers"); // matches ownership under the album-artist
    }
}
