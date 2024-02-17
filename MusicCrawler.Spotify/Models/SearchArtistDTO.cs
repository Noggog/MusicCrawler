namespace MusicCrawler.Spotify.Models;

public class ExternalUrls
{
    public required string Spotify { get; set; }
}

public class Followers
{
    public required object Href { get; set; }
    public required int Total { get; set; }
}

public class Image
{
    public required int Height { get; set; }
    public required string Url { get; set; }
    public required int Width { get; set; }
}

public class ArtistItem
{
    public required ExternalUrls? ExternalUrls { get; set; }
    public required Followers? Followers { get; set; }
    public required List<string>? Genres { get; set; }
    public required string Href { get; set; }
    public required string Id { get; set; }
    public required List<Image> Images { get; set; }
    public required string Name { get; set; }
    public required int Popularity { get; set; }
    public required string Type { get; set; }
    public required string Uri { get; set; }
}

public class Artists
{
    public required string Href { get; set; }
    public required List<ArtistItem> Items { get; set; }
}

public class SearchArtistDto
{
    public required Artists Artists { get; set; }
}