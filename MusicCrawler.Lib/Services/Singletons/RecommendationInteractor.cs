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

    public RecommendationInteractor(
        IRecommendationRepo recommendationRepo,
        ILibraryQuery libraryQuery)
    {
        _recommendationRepo = recommendationRepo;
        _libraryQuery = libraryQuery;
    }

    public async Task<IEnumerable<Recommendation>> Recommendations()
    {
        var currentPlexLibrary = await _libraryQuery.QueryAllArtistMetadata();
        var sourceArtists = currentPlexLibrary.TakeRandomly(10).Select(x => x.Key).ToList();
        var recommendations = await _recommendationRepo.RecommendArtistsFrom(
            artistKeys: sourceArtists
        );
        var artistNameSet = currentPlexLibrary.Select(it => it.Key.ArtistName).ToHashSet();
        return recommendations
            .Where(recommendedArtist => !artistNameSet.Contains(recommendedArtist.ArtistName))
            .Select(recommendedArtist => new Recommendation(recommendedArtist, sourceArtists));
    }
}