using Microsoft.AspNetCore.WebUtilities;
using MusicCrawler.Interfaces;

namespace MusicCrawler.UI;

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
    
    public async Task<IReadOnlyList<ArtistMetadata>> SearchArtistsAsync(string? term, CancellationToken cancellationToken = default)
    {
        if (term == null) return [];
        List<ArtistMetadata> artists = new();
        var url = QueryHelpers.AddQueryString("/artistsSearch", "term", term);

        await foreach (var artist in httpClient.GetFromJsonAsAsyncEnumerable<ArtistMetadata>(url, cancellationToken))
        {
            if (artist == null) continue;
            artists.Add(artist);
        }

        return artists;
    }
}