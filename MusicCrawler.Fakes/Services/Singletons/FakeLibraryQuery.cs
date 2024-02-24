using MusicCrawler.Lib;

namespace MusicCrawler.Fakes.Services.Singletons;

public class FakeLibraryQuery : ILibraryQuery
{
    private readonly ArtistPackage[] _packages;

    public FakeLibraryQuery(params ArtistPackage[] packages)
    {
        _packages = packages;
    }

    public Task<ArtistPackage> QueryArtistPackage(ArtistKey artistKey)
    {
        throw new NotImplementedException();
    }

    public async Task<ArtistPackage[]> QueryAllData()
    {
        return _packages;
    }

    public async Task<ArtistMetadata[]> QueryAllArtistMetadata()
    {
        return _packages.Select(it => it.Metadata).ToArray();
    }
}