namespace MusicCrawler.Lib;

public record AlbumKey(string AlbumName);
public record Album(AlbumKey Key, string? AlbumArt);