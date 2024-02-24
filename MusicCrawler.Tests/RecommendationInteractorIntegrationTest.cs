using System.Reactive.Linq;
using Autofac;
using FluentAssertions;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;
using Xunit;

namespace MusicCrawler.Tests;

public class RecommendationInteractorIntegrationTest
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
            .ToJson()
            .Should().Be(
                new Recommendation[]
                {
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Flagboy Giz"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "The Wild Tchoupitoulas"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Lord Kossity"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Classified"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Dilated Peoples"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Capleton"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Ombladon"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Bitza"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Damian Marley"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Stephen Marley"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Capleton"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Drag-On"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Spectacular"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Conquering Sound"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "The Mouse Outfit"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "IAMDDB"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Fox"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "L'Entourloop"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Rodney P"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    ),
                    new Recommendation(
                        Key: new ArtistKey(
                            ArtistName: "Mad Lion"),
                        SourceArtists: new[]
                        {
                            new ArtistKey(
                                ArtistName: "fakeArtistName1")
                        }
                    )
                }.ToJson());
    }
}