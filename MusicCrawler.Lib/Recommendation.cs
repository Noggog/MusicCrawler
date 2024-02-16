namespace MusicCrawler.Lib;

public record Recommendation(ArtistKey Key, IEnumerable<ArtistKey> SourceArtists);