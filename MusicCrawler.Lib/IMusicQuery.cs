namespace MusicCrawler.Lib;

/// <summary>
/// Interface for a source from which music can be looked up, which may or may not be "infinite"
/// </summary>
public interface IMusicQuery
{
    Task<ArtistPackage> QueryArtistPackage(ArtistKey artistKey);
}