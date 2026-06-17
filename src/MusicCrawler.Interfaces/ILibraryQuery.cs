namespace MusicCrawler.Interfaces;

public interface ILibraryQuery
{
    Task<ArtistMetadata[]> QueryAllArtistMetadata();

    /// <summary>Every owned artist's album titles, pulled from the Plex library.</summary>
    Task<ArtistAlbums[]> QueryAllAlbums();
}
