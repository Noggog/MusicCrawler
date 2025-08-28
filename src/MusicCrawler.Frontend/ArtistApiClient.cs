using MusicCrawler.Interfaces;

namespace MusicCrawler.Frontend;

public class ArtistApiClient(HttpClient httpClient)
{
    public async Task<ArtistMetadata[]> GetArtistsAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        List<ArtistMetadata>? artists = null;

        await foreach (var artist in httpClient.GetFromJsonAsAsyncEnumerable<ArtistMetadata>("/artists", cancellationToken))
        {
            if (artists?.Count >= maxItems)
            {
                break;
            }
            if (artist is not null)
            {
                artists ??= [];
                artists.Add(artist);
            }
        }

        return artists?.ToArray() ?? [];
    }
}