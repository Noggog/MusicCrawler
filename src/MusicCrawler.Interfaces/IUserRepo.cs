namespace MusicCrawler.Interfaces;

/// <summary>
/// Persists app users keyed by OIDC subject. Populated on login (no self-registration) — the
/// IdP is the source of truth for identity; this store exists so taste/seeds have something stable
/// to hang off and so we can show "first seen / last login".
/// </summary>
public interface IUserRepo
{
    /// <summary>
    /// Upserts the user on login: profile fields and last-login are refreshed every time;
    /// first-seen is set once on initial insert.
    /// </summary>
    Task UpsertOnLogin(AppUser user);

    Task<AppUser?> Get(string subject);
}
