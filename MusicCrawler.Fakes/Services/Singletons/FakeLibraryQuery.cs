using MusicCrawler.Lib;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeLibraryQuery : ILibraryQuery
{
    public Task<ArtistPackage> QueryArtistPackage(ArtistKey artistKey)
    {
        throw new NotImplementedException();
    }

    public async Task<ArtistPackage[]> QueryAllData()
    {
        return new[]
        {
            new ArtistPackage(
                Metadata: new ArtistMetadata(
                    Key: new ArtistKey("fakeArtistName1"),
                    ArtistImageUrl: "fakeArtistImageUrl1"),
                Albums: new[]
                {
                    new Album(
                        Key: new AlbumKey(
                            "fakeAlbumName1"),
                        AlbumArt: "fakeAlbumArt1")
                }
            )
        };
    }
}