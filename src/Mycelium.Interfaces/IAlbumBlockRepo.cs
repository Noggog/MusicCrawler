namespace Mycelium.Interfaces;

/// <summary>
/// One globally blocked album: a release nobody should be offered — a bad Deezer entry, a reissue
/// that duplicates something owned, a record the library has decided against carrying. Distinct from
/// an <see cref="AlbumMatchOverride"/> (which asserts "we already have this") and from a per-user
/// "meh" (<see cref="DiscoveryStatus.Disliked"/>, which only hides it from the one user who said so).
/// <see cref="BlockedBy"/> is the user who placed it, kept for audit — anyone may lift it.
/// </summary>
public record AlbumBlock(string Artist, string Album, string? BlockedBy = null);

/// <summary>
/// Durable, global store of blocked albums. Consulted when serving every album surface (the
/// missing-album feed, a liked artist's inline albums, the Artists-page discography) so a blocked
/// release stops being offered to <em>everyone</em>, not just the user who blocked it.
///
/// Blocks are held here rather than applied to <see cref="IMissingAlbumRepo"/> so the nightly Deezer
/// re-diff can't resurrect them, and so the album's row (and the Deezer id the downloader needs)
/// survives for anyone who had already queued it to buy.
/// </summary>
public interface IAlbumBlockRepo
{
    /// <summary>Every block on record.</summary>
    Task<AlbumBlock[]> GetAll();

    /// <summary>Records a block. Idempotent for the same (artist, album).</summary>
    Task Add(AlbumBlock block);

    /// <summary>Lifts a block, returning the album to everyone's feeds.</summary>
    Task Remove(string artist, string album);
}
