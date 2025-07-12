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

    public async Task<PlexLibrary[]> GetLibraries()
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections";
        var response = await httpClient.GetStringAsync(url);
        Console.WriteLine($"--> GetLibraries\n\t{response}<--");
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Directory"].ToObject<PlexLibrary[]>();
    }

    public async Task<PlexMusicArtist[]> GetMusicArtists(int library)
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections/{library}/all";
        var response = await httpClient.GetStringAsync(url);
        Console.WriteLine($"--> GetMusicArtists\n\t{response}\n<--");
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Metadata"].ToObject<PlexMusicArtist[]>();
    }

    public async Task<PlexRecentlyAddedItem[]> GetRecentlyAdded(int libraryKey, int maxResults = 5)
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections/{libraryKey}/recentlyAdded?X-Plex-Container-Start=0&X-Plex-Container-Size={maxResults}";
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Metadata"].ToObject<PlexRecentlyAddedItem[]>();
    }
}

public class PlexLibrary
{
    public int Key { get; set; }
    public string Title { get; set; }
    public string Type { get; set; }
}

public class PlexRecentlyAddedItem
{
    public string Title { get; set; }
}

public record PlexMusicArtist
{
    public int RatingKey { get; set; }
    public string Key { get; set; }
    public string Guid { get; set; }
    public string Title { get; set; }
}