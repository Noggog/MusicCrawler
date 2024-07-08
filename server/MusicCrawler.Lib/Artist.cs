namespace MusicCrawler.Lib;

public record Recommendation(ArtistKey ArtistKey, ArtistKey[] SourceArtists);

public record ArtistKey(string ArtistName);

public record ArtistMetadata(ArtistKey ArtistKey, string? ArtistImageUrl);

public record ArtistPackage(ArtistMetadata Metadata, Album[] Albums);