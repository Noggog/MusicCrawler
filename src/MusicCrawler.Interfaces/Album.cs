namespace MusicCrawler.Interfaces;

public record AlbumKey(string AlbumName);
public record Album(AlbumKey Key, string? AlbumArt);