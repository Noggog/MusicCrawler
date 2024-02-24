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
            ),
            new ArtistPackage(
                Metadata: new ArtistMetadata(
                    Key: new ArtistKey("fakeArtistName2"),
                    ArtistImageUrl: "fakeArtistImageUrl2"),
                Albums: new[]
                {
                    new Album(
                        Key: new AlbumKey(
                            "fakeAlbumName2"),
                        AlbumArt: "fakeAlbumArt2")
                }
            )
        };
    }

    public async Task<ArtistMetadata[]> QueryAllArtistMetadata()
    {
        return new[]
        {
            new ArtistMetadata(
                Key: new ArtistKey("fakeArtistName1"),
                ArtistImageUrl: "fakeArtistImageUrl1"),
            new ArtistMetadata(
                Key: new ArtistKey("fakeArtistName2"),
                ArtistImageUrl: "fakeArtistImageUrl2"),
        };
    }
}