using Autofac;
using FluentAssertions;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;
using Noggog.Testing.AutoFixture;
using Xunit;

namespace MusicCrawler.Tests;

public class RecommendationInteractorTest
{
    [Theory(Skip = "Leaves background processes"), DefaultAutoData]
    public async Task Typical(ArtistPackage artistPackage1, ArtistPackage artistPackage2, ArtistPackage artistPackage3)
    {
        // # Given
        var containerBuilder =
            FakeBaseIocContainer.fakeBaseIocContainer();
        containerBuilder
            .RegisterInstance(
                new FakeLibraryQuery(artistPackage1, artistPackage2))
            .AsImplementedInterfaces()
            .SingleInstance();
        containerBuilder
            .RegisterInstance(
                new FakeRecommendationRepo(
                    sourceArtistToRecommendedArtistDict: new Dictionary<ArtistKey, ArtistKey[]>
                    {
                        {
                            artistPackage1.Metadata.ArtistKey,
                            new[]
                            {
                                artistPackage2.Metadata.ArtistKey,
                                artistPackage3.Metadata.ArtistKey,
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
                        ArtistKey: artistPackage3.Metadata.ArtistKey,
                        SourceArtists: new[]
                        {
                            artistPackage1.Metadata.ArtistKey
                        }
                    )
                }.ToJson());
    }
}