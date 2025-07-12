namespace MusicCrawler.Lib;

/// <summary>
/// Interface for a source that can act as a user library, with a "finite" amount of artists/albums
/// </summary>
public interface ILibraryQuery : IMusicQuery
{
    Task<ArtistPackage[]> QueryAllData();
    Task<ArtistMetadata[]> QueryAllArtistMetadata();
}