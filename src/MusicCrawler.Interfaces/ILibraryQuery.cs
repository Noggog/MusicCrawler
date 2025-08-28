namespace MusicCrawler.Interfaces;

public interface ILibraryQuery
{
    Task<ArtistMetadata[]> QueryAllArtistMetadata();
}