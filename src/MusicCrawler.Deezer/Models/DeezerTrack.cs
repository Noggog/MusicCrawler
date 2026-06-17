namespace MusicCrawler.Deezer.Models;

/// <summary>
/// A track as returned by the Deezer public API. <c>preview</c> is a ~30-second MP3 URL that plays
/// in a plain HTML5 &lt;audio&gt; element with no auth/login (the robust alternative to the widget).
/// Field names are lower-case to match the JSON verbatim (Newtonsoft binds by exact name).
/// </summary>
public class DeezerTrack
{
    public long id { get; set; }
    public string? title { get; set; }
    public string? preview { get; set; }
    public string? link { get; set; }
}

/// <summary>Envelope Deezer wraps track-list responses in: <c>{ "data": [ ... ] }</c>.</summary>
public class DeezerTrackList
{
    public List<DeezerTrack> data { get; set; } = new();
}
