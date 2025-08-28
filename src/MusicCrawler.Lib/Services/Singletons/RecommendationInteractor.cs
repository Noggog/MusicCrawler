using MusicCrawler.Interfaces;

namespace MusicCrawler.Lib.Services.Singletons;

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
    private readonly IRecommendationPersistanceRepo _recommendationPersistanceRepo;

    public RecommendationInteractor(
        IRecommendationProvider recommendationProvider,
        ILibraryQuery libraryQuery,
        IRecommendationPersistanceRepo recommendationPersistanceRepo)
    {
        _recommendationProvider = recommendationProvider;
        _libraryQuery = libraryQuery;
        _recommendationPersistanceRepo = recommendationPersistanceRepo;
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
            
        await _recommendationPersistanceRepo.AddRecommendations(newRecommendations);

        return (await _recommendationPersistanceRepo.GetRecommendations())
            .Select(x =>
                new Recommendation(
                    ArtistKey: x.ArtistKey,
                    SourceArtists: x.SourceArtists)
            );
    }
}