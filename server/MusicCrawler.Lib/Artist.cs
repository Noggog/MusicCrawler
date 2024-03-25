namespace MusicCrawler.Lib;

public record Recommendation(ArtistKey Key, ArtistKey[] SourceArtists);

public record ArtistKey(string ArtistName);

public record ArtistMetadata(ArtistKey Key, string? ArtistImageUrl);

public record ArtistPackage(ArtistMetadata Metadata, Album[] Albums);