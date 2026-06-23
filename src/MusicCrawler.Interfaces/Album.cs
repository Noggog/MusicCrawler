namespace MusicCrawler.Interfaces;

public record AlbumKey(string AlbumName);
public record Album(AlbumKey Key, string? AlbumArt);

/// <summary>The owned albums for one artist, as pulled from the Plex library catalog.</summary>
public record ArtistAlbums(ArtistKey Artist, IReadOnlyList<string> Albums);

/// <summary>
/// An album that exists on Deezer for an artist the user owns, but isn't in the library — a
/// candidate to acquire so an owned band stays current. A global fact about the library (not
/// per-user); the per-user verdict on it lives in <see cref="AlbumRating"/>.
///
/// <see cref="Artist"/> is the band whose discography surfaced it (where it shows in the feed).
/// <see cref="AlbumArtist"/> is the album's real credited act per Deezer — for a collaboration
/// surfaced via one member (e.g. a duo record under "Milo") these differ, and the library files
/// the album under the album-artist, so that is the key to match ownership against.
/// </summary>
public record MissingAlbum(ArtistKey Artist, AlbumKey Album, string? AlbumArt, long DeezerAlbumId, ArtistKey? AlbumArtist = null)
{
    /// <summary>The artist the library files this album under — <see cref="AlbumArtist"/> when known,
    /// else <see cref="Artist"/> (non-collaboration albums are filed under the listing artist).</summary>
    public ArtistKey MatchArtist => AlbumArtist ?? Artist;
}

/// <summary>
/// Canonical (artist, album) identity used to match a user's album verdict against a missing album.
/// One definition shared by the rating store and the feed filter so they never drift.
/// </summary>
public static class AlbumRatingKey
{
    public static string For(string artist, string album) =>
        $"{artist.ToLowerInvariant()} {album.ToLowerInvariant()}";
}
