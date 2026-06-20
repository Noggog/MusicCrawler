using System.Text;

namespace MusicCrawler.Interfaces;

/// <summary>
/// The seam for stamping a user's taste verdict onto an artist in the library backend (Plex) as a
/// collection membership — e.g. a thumbs-up writes "&lt;user&gt;_liked". Best-effort and additive: it
/// merges with any existing tags and never throws, so a tagging failure can't break the rating it
/// accompanies.
/// </summary>
public interface IArtistTagger
{
    /// <summary>Adds <paramref name="tag"/> to <paramref name="artistName"/> if not already present.</summary>
    Task AddTag(string artistName, string tag);
}

/// <summary>
/// Builds the per-user collection tag for a taste verdict — "&lt;username&gt;_liked" /
/// "&lt;username&gt;_disliked". The username is trimmed of any email domain and reduced to [a-z0-9_] so
/// the tag is clean and collision-resistant; returns null when there's no usable username (so the caller
/// skips tagging).
/// </summary>
public static class ArtistTag
{
    public static string? For(string? username, DiscoveryStatus status)
    {
        var prefix = Sanitize(username);
        if (prefix.Length == 0)
        {
            return null;
        }

        var verdict = status == DiscoveryStatus.Liked ? "liked" : "disliked";
        return $"{prefix}_{verdict}";
    }

    /// <summary>
    /// Whether a collection name is one this app manages — the "_liked"/"_disliked" suffix namespace.
    /// Used by the dev wipe to strip only our tags and leave hand-made collections alone. Coarse on
    /// purpose: any name with that suffix is treated as ours (we can't enumerate every username that
    /// ever rated), which is the right call for a "clean slate" reset.
    /// </summary>
    public static bool IsManaged(string? label) =>
        label != null
        && (label.EndsWith("_liked", StringComparison.OrdinalIgnoreCase)
            || label.EndsWith("_disliked", StringComparison.OrdinalIgnoreCase));

    private static string Sanitize(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "";
        }

        // Email-style usernames trim to the local part before '@'.
        var at = username.IndexOf('@');
        var local = at >= 0 ? username[..at] : username;

        var sb = new StringBuilder(local.Length);
        foreach (var c in local.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
