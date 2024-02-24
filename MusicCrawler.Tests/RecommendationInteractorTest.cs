using FluentAssertions;
using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;
using Xunit;

namespace MusicCrawler.Tests;

public class RecommendationInteractorTest
{
    [Theory, FakeData(false)]
    public async Task typical(
        RecommendationInteractor sut
    )
    {
        // # When
        var result = await sut.Recommendations();
        // # Then
        result
            .ToJson()
            .Should().Be(
                new Recommendation[]
                {
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "fakeArtistName2"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    )
                }.ToJson());
    }
}