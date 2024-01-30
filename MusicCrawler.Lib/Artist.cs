namespace MusicCrawler.Lib;

public record ArtistKey(string ArtistName);

public record ArtistMetadata(ArtistKey Key, string? ArtistImageUrl);

public record ArtistPackage(ArtistMetadata Metadata, Album[] Albums);