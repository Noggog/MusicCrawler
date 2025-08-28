namespace MusicCrawler.Interfaces;

public record SongKey(string SongName);
public record Song(SongKey Key, TimeSpan SongLength);