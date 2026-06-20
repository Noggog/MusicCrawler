using System.Security.Claims;

namespace MusicCrawler.Backend.Services.Auth;

/// <summary>
/// The set of users allowed to see and use the in-app dev panel (Plex tag maintenance, similarity
/// debugging). Sourced from the <c>DEV_USERNAMES</c> env var — a comma-separated list of
/// <c>preferred_username</c>s. Empty means "nobody", so the panel and its endpoints are closed to
/// everyone unless explicitly opted in. Kept out of hardcoded config so who counts as a dev is a
/// per-deployment decision.
/// </summary>
public sealed class DevUsers
{
    private readonly HashSet<string> _usernames;

    public DevUsers(string? configured)
    {
        _usernames = (configured ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.ToLowerInvariant())
            .ToHashSet();
    }

    /// <summary>The configured dev usernames (lowercased) — for diagnostics/logging.</summary>
    public IReadOnlyCollection<string> Configured => _usernames;

    /// <summary>Whether the signed-in user's <c>preferred_username</c> is in the configured dev set.</summary>
    public bool Includes(ClaimsPrincipal user)
    {
        var username = user.FindFirst("preferred_username")?.Value?.ToLowerInvariant();
        return username != null && _usernames.Contains(username);
    }
}
