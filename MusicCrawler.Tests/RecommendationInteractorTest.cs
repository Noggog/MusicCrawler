using FluentAssertions;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;
using Xunit;

namespace MusicCrawler.Tests;

public class RecommendationInteractorTest
{
    [Fact]
    public async Task typical()
    {
        // # Given
        var sut =
            new RecommendationInteractor(
                new FakeRecommendationRepo(),
                new FakeLibraryQuery()
            );
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