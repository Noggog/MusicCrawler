using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Resolves display metadata for recommended artists from the sources that <em>have</em> it, rather
/// than only from whichever source recommended the artist. A ListenBrainz-only recommendation carries
/// no image (ListenBrainz has none), but Deezer almost certainly has a photo for that same artist —
/// this pipeline fills it in. Resolution is per-name and cached (30 days), so after the first warm it
/// is effectively free; the first pass is bounded-parallel to avoid hammering Deezer.
///
/// Today the only metadata that differs by source is the image (Deezer), so that's all this fills;
/// it's the seam to add other cross-source meta (e.g. canonical links/ids) as sources grow.
/// </summary>
public class ArtistMetaEnricher
{
    private const int MaxConcurrency = 8;

    private readonly DeezerArtistResolver _deezer;
    private readonly ILogger<ArtistMetaEnricher> _logger;

    public ArtistMetaEnricher(DeezerArtistResolver deezer, ILogger<ArtistMetaEnricher> logger)
    {
        _deezer = deezer;
        _logger = logger;
    }

    /// <summary>Fill missing images on a unified related-artists list (the Related tab / read path).</summary>
    public async Task<UnifiedRelations> EnrichImages(UnifiedRelations relations)
    {
        var images = await ResolveImages(
            relations.Related.Where(r => r.ImageUrl == null).Select(r => r.ArtistKey.ArtistName));
        if (images.Count == 0) return relations;

        var enriched = relations.Related
            .Select(r => r.ImageUrl == null && images.TryGetValue(r.ArtistKey.ArtistName, out var img)
                ? r with { ImageUrl = img }
                : r)
            .ToArray();
        return relations with { Related = enriched };
    }

    /// <summary>
    /// Fill missing images on a feed page (the discovery surface). Only artist items are touched —
    /// missing-album items get their art via their Deezer album id, not an artist-name lookup.
    /// </summary>
    public async Task<IReadOnlyList<FeedItem>> EnrichImages(IReadOnlyList<FeedItem> items)
    {
        var images = await ResolveImages(
            items.Where(i => i.ImageUrl == null && i.Album == null).Select(i => i.Artist.ArtistName));
        if (images.Count == 0) return items;

        return items
            .Select(i => i.ImageUrl == null && i.Album == null && images.TryGetValue(i.Artist.ArtistName, out var img)
                ? i with { ImageUrl = img }
                : i)
            .ToArray();
    }

    /// <summary>Resolve a Deezer image per distinct name (bounded-parallel); returns only those found.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveImages(IEnumerable<string> names)
    {
        var distinct = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinct.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        var resolved = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(MaxConcurrency);

        await Task.WhenAll(distinct.Select(async name =>
        {
            await gate.WaitAsync();
            try
            {
                var image = await _deezer.ResolveImageUrl(name);
                if (image != null)
                {
                    resolved[name] = image;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Image enrichment failed for {Artist}", name);
            }
            finally
            {
                gate.Release();
            }
        }));

        return resolved;
    }
}
