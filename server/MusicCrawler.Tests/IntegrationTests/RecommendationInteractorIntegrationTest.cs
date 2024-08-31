using Autofac;
using FluentAssertions;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;
using Noggog.Testing.AutoFixture;
using Xunit;

namespace MusicCrawler.Tests.IntegrationTests;

public class RecommendationInteractorIntegrationTest
{
    [Theory(Skip = "Leaves background processes"), DefaultAutoData]
    public async Task Typical(ArtistPackage artistPackage1)
    {
        // # Given
        var containerBuilder =
            FakeBaseIocContainer.fakeBaseIocContainer();
        containerBuilder
            .RegisterInstance(
                new FakeLibraryQuery(artistPackage1))
            .AsImplementedInterfaces()
            .SingleInstance();
        containerBuilder
            .RegisterInstance(
                new FakeSpotifyApi())
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
            .Take(3)
            .ToJson()
            .Should().Be(
                new Recommendation[]
                {
                    new Recommendation(
                        ArtistKey: new ArtistKey(
                            ArtistName: "Flagboy Giz"),
                        SourceArtists: new[]
                        {
                            artistPackage1.Metadata.ArtistKey
                        }
                    ),
                    new Recommendation(
                        ArtistKey: new ArtistKey(
                            ArtistName: "The Wild Tchoupitoulas"),
                        SourceArtists: new[]
                        {
                            artistPackage1.Metadata.ArtistKey
                        }
                    ),
                    new Recommendation(
                        ArtistKey: new ArtistKey(
                            ArtistName: "Lord Kossity"),
                        SourceArtists: new[]
                        {
                            artistPackage1.Metadata.ArtistKey
                        }
                    )
                }.ToJson());
    }
}