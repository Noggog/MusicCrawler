using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/**
 * requirements:
 *   https://github.com/Noggog/MusicCrawler/issues/16
 * TODO: Should depend on some derivative of user input, for when the user wants to stop getting recommendations for certain artists.
 * TODO: Should also investigate if there are new albums for artists already in the library.
 */
public class RecommendationInteractor
{
    private readonly IRecommendationProvider _recommendationProvider;
    private readonly ILibraryQuery _libraryQuery;
    private readonly IRecommendationPersistenceRepo _recommendationPersistenceRepo;

    public RecommendationInteractor(
        IRecommendationProvider recommendationProvider,
        ILibraryQuery libraryQuery,
        IRecommendationPersistenceRepo recommendationPersistenceRepo)
    {
        _recommendationProvider = recommendationProvider;
        _libraryQuery = libraryQuery;
        _recommendationPersistenceRepo = recommendationPersistenceRepo;
    }

    public async Task<IEnumerable<Recommendation>> Recommendations()
    {
        var currentPlexLibrary = await _libraryQuery.QueryAllArtistMetadata();
        var sourceArtists = currentPlexLibrary.Take(10).Select(x => x.ArtistKey).ToList();
        var recommendations = await _recommendationProvider.RecommendArtistsFrom(
            artistKeys: sourceArtists
        );
        var artistNamesFromLibrary = currentPlexLibrary.Select(it => it.ArtistKey.ArtistName).ToHashSet();
        var newRecommendations =
            recommendations
                .Where(recommendedArtist => !artistNamesFromLibrary.Contains(recommendedArtist.ArtistKey.ArtistName))
                .ToArray();
        Console.WriteLine($"Adding {newRecommendations.Length} Recommendations");
            
        await _recommendationPersistenceRepo.AddRecommendations(newRecommendations);

        return (await _recommendationPersistenceRepo.GetRecommendations())
            .Select(x =>
                new Recommendation(
                    ArtistKey: x.ArtistKey,
                    SourceArtists: x.SourceArtists)
            );
    }
}