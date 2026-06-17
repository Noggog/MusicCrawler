namespace MusicCrawler.Deezer.Models;

/// <summary>
/// An album as returned by the Deezer public API (<c>GET /artist/{id}/albums</c>). Field names are
/// lower/snake-case to match the JSON verbatim (Newtonsoft binds by exact name). <c>record_type</c>
/// is one of "album" / "single" / "ep" / "compilation" — the missing-album diff keeps only "album"
/// to avoid drowning the feed in singles.
/// </summary>
public class DeezerAlbum
{
    public long id { get; set; }
    public string? title { get; set; }
    public string? record_type { get; set; }

    // Deezer ships several cover sizes; we prefer the largest available.
    public string? cover_xl { get; set; }
    public string? cover_big { get; set; }
    public string? cover_medium { get; set; }

    /// <summary>Best available cover image URL, largest first, or null if Deezer supplied none.</summary>
    public string? BestCoverUrl => cover_xl ?? cover_big ?? cover_medium;
}

/// <summary>Envelope Deezer wraps album-list responses in: <c>{ "data": [ ... ] }</c>.</summary>
public class DeezerAlbumList
{
    public List<DeezerAlbum> data { get; set; } = new();
}
