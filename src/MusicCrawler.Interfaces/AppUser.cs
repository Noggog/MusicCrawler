namespace MusicCrawler.Interfaces;

/// <summary>
/// A user of the app, identified by the OIDC <paramref name="Subject"/> (the stable "sub" claim).
/// Profile fields mirror the IdP and are refreshed on each login; the app owns only taste/seeds,
/// never credentials — authentication lives entirely in the OIDC provider.
/// </summary>
public record AppUser(
    string Subject,
    string? Username,
    string? Email,
    string? DisplayName,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastLoginAt);
