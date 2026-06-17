namespace MusicCrawler.Interfaces;

public record AlbumKey(string AlbumName);
public record Album(AlbumKey Key, string? AlbumArt);

/// <summary>The owned albums for one artist, as pulled from the Plex library catalog.</summary>
public record ArtistAlbums(ArtistKey Artist, IReadOnlyList<string> Albums);

/// <summary>
/// An album that exists on Deezer for an artist the user owns, but isn't in the library — a
/// candidate to acquire so an owned band stays current. A global fact about the library (not
/// per-user); the per-user verdict on it lives in <see cref="AlbumRating"/>.
/// </summary>
public record MissingAlbum(ArtistKey Artist, AlbumKey Album, string? AlbumArt);

/// <summary>
/// Canonical (artist, album) identity used to match a user's album verdict against a missing album.
/// One definition shared by the rating store and the feed filter so they never drift.
/// </summary>
public static class AlbumRatingKey
{
    public static string For(string artist, string album) =>
        $"{artist.ToLowerInvariant()} {album.ToLowerInvariant()}";
}
