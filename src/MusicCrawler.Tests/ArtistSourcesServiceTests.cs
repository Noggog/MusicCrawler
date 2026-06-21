using FluentAssertions;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class ArtistSourcesServiceTests
{
    private static readonly ArtistKey Radiohead = new("Radiohead");
    private const string Mbid = "a74b1b7f-71a5-4011-9441-d0b5e4122711";

    private static ISourceIdentityCorrector Corrector(string source, SourceIdentity? current)
    {
        var c = Substitute.For<ISourceIdentityCorrector>();
        c.Source.Returns(source);
        c.GetCurrent(Arg.Any<ArtistKey>()).Returns(current);
        return c;
    }

    [Fact]
    public async Task Composes_resolved_sources_in_display_order_with_a_derived_listenbrainz_link()
    {
        var deezer = new SourceIdentity("deezer", "399", "Radiohead", "4,200,000 fans",
            "https://www.deezer.com/artist/399", "img", IsOverride: false, Correctable: true);
        var mb = new SourceIdentity("musicbrainz", Mbid, "Radiohead", null,
            $"https://musicbrainz.org/artist/{Mbid}", null, IsOverride: false, Correctable: true);

        // Pass musicbrainz first to prove the service imposes its own display order.
        var sut = new ArtistSourcesService(new[] { Corrector("musicbrainz", mb), Corrector("deezer", deezer) });

        var result = await sut.Get(Radiohead);

        result.Sources.Select(s => s.Source).Should().Equal("deezer", "musicbrainz", "listenbrainz");
        var lb = result.Sources.Single(s => s.Source == "listenbrainz");
        lb.Id.Should().Be(Mbid); // derived from the MBID
        lb.Link.Should().Be($"https://listenbrainz.org/artist/{Mbid}");
        lb.Correctable.Should().BeFalse();
    }

    [Fact]
    public async Task Unresolved_source_yields_a_correctable_placeholder_and_no_listenbrainz_row()
    {
        // MusicBrainz unresolved (null) → placeholder, and with no MBID there's no ListenBrainz link.
        var sut = new ArtistSourcesService(new[]
        {
            Corrector("deezer", null),
            Corrector("musicbrainz", null),
        });

        var result = await sut.Get(Radiohead);

        result.Sources.Select(s => s.Source).Should().Equal("deezer", "musicbrainz");
        result.Sources.Should().OnlyContain(s => s.Id == null && s.Correctable && !s.IsOverride);
    }
}
