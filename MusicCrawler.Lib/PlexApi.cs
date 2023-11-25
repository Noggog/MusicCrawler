using Newtonsoft.Json.Linq;

namespace MusicCrawler.Lib;

public class PlexApi
{
    private readonly HttpClient httpClient;
    private readonly string baseUri;

    public PlexApi(string baseUri, string token)
    {
        this.baseUri = baseUri;
        this.httpClient = new HttpClient();
        this.httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        this.httpClient.DefaultRequestHeaders.Add("X-Plex-Token", token);
    }

    public async Task<Library[]> GetLibraries()
    {
        string url = $"{baseUri}/library/sections";
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Directory"].ToObject<Library[]>();
    }

    public async Task<Item[]> GetRecentlyAdded(string libraryKey, int maxResults = 5)
    {
        string url = $"{baseUri}/library/sections/{libraryKey}/recentlyAdded?X-Plex-Container-Start=0&X-Plex-Container-Size={maxResults}";
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Metadata"].ToObject<Item[]>();
    }
}

public class Library
{
    public string Key { get; set; }
    public string Title { get; set; }
}

public class Item
{
    public string Title { get; set; }
    // Add other properties you want to retrieve
}