namespace MusicCrawler.Interfaces;

/// <summary>
/// Global store of albums that exist on Deezer for owned artists but aren't in the library — the
/// raw material for each user's "missing albums" feed. A fact about the shared library, not
/// per-user; the per-user verdict lives in <see cref="IUserAlbumRatingRepo"/>. One doc per
/// (artist, album); populated by the missing-album sync job.
/// </summary>
public interface IMissingAlbumRepo
{
    /// <summary>
    /// Replaces the full missing-album set for one artist (deletes the artist's prior rows, inserts
    /// the current ones). Albums acquired since the last run simply stop being supplied, so they
    /// drop out — keeping the feed honest with no separate cleanup pass.
    /// </summary>
    Task ReplaceForArtist(string artistName, IReadOnlyList<MissingAlbum> missing);

    /// <summary>Every missing album, ordered by artist then album, for building a user's feed.</summary>
    Task<MissingAlbum[]> GetAll();
}
