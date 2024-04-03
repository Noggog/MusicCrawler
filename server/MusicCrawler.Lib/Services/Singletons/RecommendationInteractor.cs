namespace MusicCrawler.Lib.Services.Singletons;

/**
 * requirements:
 *   https://github.com/Noggog/MusicCrawler/issues/16
 * TODO: Should depend on some derivative of user input, for when the user wants to stop getting recommendations for certain artists.
 * TODO: Should also investigate if there are new albums for artists already in the library.
 */
public class RecommendationInteractor
{
    private readonly IRecommendationRepo _recommendationRepo;
    private readonly ILibraryQuery _libraryQuery;
    private readonly IRecommendationPersistanceRepo _recommendationPersistanceRepo;

    public RecommendationInteractor(
        IRecommendationRepo recommendationRepo,
        ILibraryQuery libraryQuery,
        IRecommendationPersistanceRepo recommendationPersistanceRepo)
    {
        _recommendationRepo = recommendationRepo;
        _libraryQuery = libraryQuery;
        _recommendationPersistanceRepo = recommendationPersistanceRepo;
    }

    public async Task AccumulateRecommendations()
    {
        var currentPlexLibrary = await _libraryQuery.QueryAllArtistMetadata();
        var sourceArtists = currentPlexLibrary.TakeRandomly(10).Select(x => x.Key).ToList();
        var recommendations = await _recommendationRepo.RecommendArtistsFrom(
            artistKeys: sourceArtists
        );
        var artistNamesFromLibrary = currentPlexLibrary.Select(it => it.Key.ArtistName).ToHashSet();
        var newRecommendations =
            recommendations
                .Where(recommendedArtist => !artistNamesFromLibrary.Contains(recommendedArtist.Key.ArtistName));

        await _recommendationPersistanceRepo.AddToMap(newRecommendations.ToMap().Also(x => Console.WriteLine("Adding " + x.Count + " Recommendations")));
    }

    public async Task<IEnumerable<Recommendation>> Recommendations()
    {
        return (await _recommendationPersistanceRepo.GetMap())
            .Select(x =>
                new Recommendation(
                    Key: x.Key,
                    SourceArtists: x.Value)
            );
    }
}