using Autofac;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;
using MusicCrawler.MongoDB.Services.Data;
using Noggog.Testing.AutoFixture;
using Xunit;

namespace MusicCrawler.Tests;

public class RecommendationPersistanceRepoTest
{
    [Theory, DefaultAutoData]
    public async Task Typical(ArtistPackage artistPackage1, ArtistPackage artistPackage2, ArtistPackage artistPackage3)
    {
        // # Given
        var containerBuilder =
            FakeBaseIocContainer.fakeBaseIocContainer();
        containerBuilder
            .RegisterInstance(
                new FakeMongoDbProvider())
            .AsImplementedInterfaces()
            .SingleInstance();
        var dictionary =
            new Dictionary<ArtistKey, ArtistKey[]>
            {
                {
                    artistPackage1.Metadata.Key,
                    new[]
                    {
                        artistPackage2.Metadata.Key,
                        artistPackage3.Metadata.Key,
                    }
                }
            };
        var sut =
            containerBuilder.Build().Resolve<RecommendationPersistanceRepo>();
        // # When
        await sut.AddToMap(dictionary);
        await sut.AddToMap(dictionary);
        await sut.GetMap();
        // # Then
        // There should be no exception
    }
}