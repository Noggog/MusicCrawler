using Newtonsoft.Json.Linq;

namespace MusicCrawler.Plex.Services.Singletons;

public class PlexApi
{
    private readonly PlexEndpointInfo _endpointInfo;
    private readonly PlexClientInfo _clientInfo;
    private readonly HttpClient httpClient;

    public PlexApi(PlexEndpointInfo endpointInfo, PlexClientInfo clientInfo)
    {
        _endpointInfo = endpointInfo;
        _clientInfo = clientInfo;
        this.httpClient = new HttpClient();
        this.httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        this.httpClient.DefaultRequestHeaders.Add("X-Plex-Token", clientInfo.Token);
    }

    public async Task<Library[]> GetLibraries()
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections";
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Directory"].ToObject<Library[]>();
    }

    public async Task<MusicArtist[]> GetMusicArtists(int library)
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections/{library}/all";
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Metadata"].ToObject<MusicArtist[]>();
    }

    public async Task<RecentlyAddedItem[]> GetRecentlyAdded(int libraryKey, int maxResults = 5)
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections/{libraryKey}/recentlyAdded?X-Plex-Container-Start=0&X-Plex-Container-Size={maxResults}";
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Metadata"].ToObject<RecentlyAddedItem[]>();
    }
}

public class Library
{
    public int Key { get; set; }
    public string Title { get; set; }
}

public class RecentlyAddedItem
{
    public string Title { get; set; }
}

public record MusicArtist
{
    public int RatingKey { get; set; }
    public string Key { get; set; }
    public string Guid { get; set; }
    public string Title { get; set; }
}