namespace MusicCrawler.Deezer.Models;

/// <summary>
/// An artist as returned by the Deezer public API (https://api.deezer.com). Field names are
/// lower/snake-case to match the JSON verbatim (Newtonsoft binds by exact name). Everything is
/// nullable because search misses and partial payloads are normal — callers must tolerate gaps.
/// </summary>
public class DeezerArtist
{
    public long id { get; set; }
    public string? name { get; set; }

    // Deezer ships several image sizes; we prefer the largest available.
    public string? picture_xl { get; set; }
    public string? picture_big { get; set; }
    public string? picture_medium { get; set; }

    /// <summary>Best available artist image URL, largest first, or null if Deezer supplied none.</summary>
    public string? BestImageUrl => picture_xl ?? picture_big ?? picture_medium;
}

/// <summary>Envelope Deezer wraps list responses in: <c>{ "data": [ ... ] }</c>.</summary>
public class DeezerArtistList
{
    public List<DeezerArtist> data { get; set; } = new();
}
