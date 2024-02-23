namespace MusicCrawler.Spotify.Models;

public class Artist
{
    public required ExternalUrls ExternalUrls { get; set; }
    public required string Href { get; set; }
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Uri { get; set; }
}

public class Album
{
    public required string AlbumType { get; set; }
    public required List<Artist> Artists { get; set; }
    public required List<string> AvailableMarkets { get; set; }
    public required ExternalUrls ExternalUrls { get; set; }
    public required string Href { get; set; }
    public required string Id { get; set; }
    public required List<Image> Images { get; set; }
    public required string Name { get; set; }
    public required string ReleaseDate { get; set; }
    public required string ReleaseDatePrecision { get; set; }
    public required int TotalTracks { get; set; }
    public required string Type { get; set; }
    public required string Uri { get; set; }
}

public class ExternalIds
{
    public required string Isrc { get; set; }
}

public class Track
{
    public required Album Album { get; set; }
    public required List<Artist> Artists { get; set; }
    public required List<string> AvailableMarkets { get; set; }
    public required int DiscNumber { get; set; }
    public required int DurationMs { get; set; }
    public required bool @Explicit { get; set; }
    public required ExternalIds ExternalIds { get; set; }
    public required ExternalUrls ExternalUrls { get; set; }
    public required string Href { get; set; }
    public required string Id { get; set; }
    public required bool IsLocal { get; set; }
    public required string Name { get; set; }
    public required int Popularity { get; set; }
    public required string PreviewUrl { get; set; }
    public required int TrackNumber { get; set; }
    public required string Type { get; set; }
    public required string Uri { get; set; }
}

public class Seed
{
    public required int InitialPoolSize { get; set; }
    public required int AfterFilteringSize { get; set; }
    public required int AfterRelinkingSize { get; set; }
    public required string Id { get; set; }
    public required string Type { get; set; }
    public required string Href { get; set; }
}

public class RecommendedArtistsDto
{
    public required List<Track> Tracks { get; set; }
    public required List<Seed> Seeds { get; set; }
}