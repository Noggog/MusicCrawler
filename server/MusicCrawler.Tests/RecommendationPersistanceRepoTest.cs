using Autofac;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;
using MusicCrawler.MongoDB.Services.Data;
using Noggog.Testing.AutoFixture;
using Xunit;

namespace MusicCrawler.Tests;

public class RecommendationPersistanceRepoTest
{
    [Theory(Skip = "Leaves background processes"), DefaultAutoData]
    public async Task Typical(ArtistPackage artistPackage1, ArtistPackage artistPackage2, ArtistPackage artistPackage3)
    {
        // # Given
        var containerBuilder =
            FakeBaseIocContainer.fakeBaseIocContainer();
        var recommendations =
            new List<Recommendation>
            {
                new Recommendation(
                    artistPackage1.Metadata.ArtistKey,
                    new[]
                    {
                        artistPackage2.Metadata.ArtistKey,
                        artistPackage3.Metadata.ArtistKey,
                    })
            };
        var sut =
            containerBuilder.Build().Resolve<RecommendationPersistanceRepo>();
        // # When
        await sut.AddRecommendations(recommendations);
        await sut.AddRecommendations(recommendations);
        await sut.GetRecommendations();
        // # Then
        // There should be no exception
    }
}