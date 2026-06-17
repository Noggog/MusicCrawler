using MusicCrawler.Interfaces;

namespace MusicCrawler.Deezer.Services;

/// <summary>
/// Live <see cref="IRecommendationProvider"/> backed by Deezer's "related artists" — the
/// replacement for the deprecated Spotify recommendations source. This is the on-demand path
/// (used by RecommendationInteractor); the persisted similarity graph is built separately by the
/// ingestion service, which uses <see cref="IDeezerApi"/> directly so it can also capture images.
/// </summary>
public class DeezerProvider : IRecommendationProvider
{
    private readonly IDeezerApi _deezerApi;

    public DeezerProvider(IDeezerApi deezerApi)
    {
        _deezerApi = deezerApi;
    }

    public Task<Recommendation[]> RecommendArtistsFrom(ArtistKey artistKey)
    {
        return RecommendArtistsFrom(new[] { artistKey });
    }

    public async Task<Recommendation[]> RecommendArtistsFrom(IEnumerable<ArtistKey> artistKeys)
    {
        // Map each recommended artist back to the seed(s) that surfaced it, so a single artist
        // recommended by several seeds collapses to one Recommendation with multiple sources.
        var bySeed = new Dictionary<ArtistKey, List<ArtistKey>>();

        foreach (var artistKey in artistKeys)
        {
            var artist = await _deezerApi.SearchArtist(artistKey.ArtistName);
            if (artist == null) continue;

            foreach (var related in await _deezerApi.GetRelated(artist.id))
            {
                if (string.IsNullOrWhiteSpace(related.name)) continue;
                var key = new ArtistKey(related.name);
                if (!bySeed.TryGetValue(key, out var sources))
                {
                    bySeed[key] = sources = new List<ArtistKey>();
                }
                sources.Add(artistKey);
            }
        }

        return bySeed.Select(x => new Recommendation(x.Key, x.Value.ToArray())).ToArray();
    }
}
