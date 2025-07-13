using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;

namespace MusicCrawler.Lib.Services.Singletons;

public interface ILibraryProvider
{
    Task<ArtistMetadata[]> GetAllArtistMetadata();
}

public class LibraryProvider : ILibraryProvider
{
    private readonly IDistributedCache _distributedCache;
    private readonly ILibraryQuery _libraryQuery;

    public LibraryProvider(
        IDistributedCache distributedCache,
        ILibraryQuery libraryQuery)
    {
        _distributedCache = distributedCache;
        _libraryQuery = libraryQuery;
    }
    
    public async Task<ArtistMetadata[]> GetAllArtistMetadata()
    {
        var results = await _distributedCache.GetStringAsync("artistMetadata");

        if (results != null)
        {
            return JsonConvert.DeserializeObject<ArtistMetadata[]>(results)!;
        }

        var ret = await _libraryQuery.QueryAllArtistMetadata();
        
        await _distributedCache.SetStringAsync("artistMetadata", JsonConvert.SerializeObject(ret), new DistributedCacheEntryOptions()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
        });

        return ret;
    }
}