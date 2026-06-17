namespace MusicCrawler.Plex.Services;

/// <summary>
/// Untangles Plex's multi-value artist fields. Plex joins collaborators in a single
/// <c>Title</c>/<c>ParentTitle</c> with semicolons (e.g. "Nina Simone;Hot Chip"), which would
/// otherwise surface as one bogus artist in the feed. We split on ';' only — never '/' or "feat",
/// which appear inside legitimate names ("AC/DC", "Death from Above").
/// </summary>
public static class ArtistNames
{
    private static readonly char[] Separators = { ';' };

    /// <summary>
    /// The distinct, trimmed artist names encoded in a raw Plex title. A name with no separator
    /// yields just itself; null/blank yields nothing. Order is first-seen; duplicates that differ
    /// only in case are collapsed.
    /// </summary>
    public static IEnumerable<string> Split(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim();
            if (name.Length > 0 && seen.Add(name))
            {
                yield return name;
            }
        }
    }
}
