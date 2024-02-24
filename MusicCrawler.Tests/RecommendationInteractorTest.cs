using Autofac;
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
        var containerBuilder =
            FakeBaseIocContainer.fakeBaseIocContainer();
        containerBuilder
            .RegisterInstance(
                new FakeLibraryQuery())
            .AsImplementedInterfaces()
            .SingleInstance();
        containerBuilder
            .RegisterInstance(
                new FakeRecommendationRepo(
                    dictionary: new Dictionary<ArtistKey, ArtistKey[]>
                    {
                        {
                            new ArtistKey("fakeArtistName1"), new[]
                            {
                                new ArtistKey(
                                    "fakeArtistName2"),
                                new ArtistKey(
                                    "fakeArtistName3")
                            }
                        }
                    }))
            .AsImplementedInterfaces()
            .SingleInstance();
        var sut =
            containerBuilder
                .Build()
                .Resolve<RecommendationInteractor>();
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
                            ArtistName: "fakeArtistName3"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    )
                }.ToJson());
    }
}