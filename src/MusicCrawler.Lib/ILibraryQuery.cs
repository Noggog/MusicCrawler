namespace MusicCrawler.Lib;

public interface ILibraryQuery
{
    Task<ArtistMetadata[]> QueryAllArtistMetadata();
}