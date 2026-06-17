using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Merges per-source related-artist edge sets into one cross-source list: one entry per distinct
/// artist, tagged with every source that recommended it. Pure (no I/O) so it's unit-testable.
/// </summary>
public static class RelatedArtistUnifier
{
    public static IReadOnlyList<UnifiedRelatedArtist> Unify(IReadOnlyList<ArtistRelations> perSource)
    {
        // Dedupe by artist name (case-insensitive). Keep the first encountered display name +
        // first non-null image, and collect the distinct sources per artist.
        var merged = new Dictionary<string, (string Name, string? Image, List<string> Sources)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var source in perSource)
        {
            foreach (var related in source.Related)
            {
                var name = related.ArtistKey.ArtistName;
                if (!merged.TryGetValue(name, out var entry))
                {
                    entry = (name, related.ImageUrl, new List<string>());
                }
                entry.Image ??= related.ImageUrl;
                if (!entry.Sources.Contains(source.Source))
                {
                    entry.Sources.Add(source.Source);
                }
                merged[name] = entry;
            }
        }

        return merged.Values
            .Select(e => new UnifiedRelatedArtist(new ArtistKey(e.Name), e.Image, e.Sources))
            .OrderBy(r => r.ArtistKey.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
