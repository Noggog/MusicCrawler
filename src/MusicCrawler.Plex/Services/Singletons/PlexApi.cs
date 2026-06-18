using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace MusicCrawler.Plex.Services.Singletons;

public class PlexApi
{
    private readonly PlexEndpointInfo _endpointInfo;
    private readonly PlexClientInfo _clientInfo;
    private readonly ILogger<PlexApi> _logger;
    private readonly HttpClient httpClient;

    public PlexApi(PlexEndpointInfo endpointInfo, PlexClientInfo clientInfo, ILogger<PlexApi> logger)
    {
        _endpointInfo = endpointInfo;
        _clientInfo = clientInfo;
        _logger = logger;
        this.httpClient = new HttpClient();
        this.httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        this.httpClient.DefaultRequestHeaders.Add("X-Plex-Token", clientInfo.Token);
    }

    public async Task<PlexLibrary[]> GetLibraries()
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections";
        _logger.LogDebug("Plex GetLibraries: {Url}", url);
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Directory"].ToObject<PlexLibrary[]>();
    }

    public async Task<PlexMusicArtist[]> GetMusicArtists(int library)
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections/{library}/all";
        _logger.LogDebug("Plex GetMusicArtists from library {Library}: {Url}", library, url);
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Metadata"].ToObject<PlexMusicArtist[]>();
    }

    public async Task<PlexMusicAlbum[]> GetMusicAlbums(int library)
    {
        // type=9 is the album metadata type; parentTitle carries the owning artist's name.
        string url = $"{_endpointInfo.BaseUri}/library/sections/{library}/all?type=9";
        _logger.LogDebug("Plex GetMusicAlbums from library {Library}: {Url}", library, url);
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        var metadata = data["MediaContainer"]?["Metadata"];
        return metadata?.ToObject<PlexMusicAlbum[]>() ?? Array.Empty<PlexMusicAlbum>();
    }

    public async Task<PlexRecentlyAddedItem[]> GetRecentlyAdded(int libraryKey, int maxResults = 5)
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections/{libraryKey}/recentlyAdded?X-Plex-Container-Start=0&X-Plex-Container-Size={maxResults}";
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Metadata"].ToObject<PlexRecentlyAddedItem[]>();
    }

    /// <summary>
    /// Kicks off a Plex scan of one library section (the empty-args refresh — Plex walks the section's
    /// folders for new/changed media). Fire-and-forget on Plex's side; this just issues the request.
    /// </summary>
    public async Task RefreshLibrary(int libraryKey)
    {
        string url = $"{_endpointInfo.BaseUri}/library/sections/{libraryKey}/refresh";
        _logger.LogDebug("Plex RefreshLibrary {Library}: {Url}", libraryKey, url);
        var response = await httpClient.GetAsync(url); // Plex accepts GET for section refresh
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Resolves the music library to operate on: the one named by <c>PLEX_LIBRARY</c> when it matches,
    /// otherwise the first artist-type library. Logs which path it took. Shared by the catalog reads
    /// and the post-download rescan so they always target the same section.
    /// </summary>
    public async Task<PlexLibrary> ResolveLibrary()
    {
        var plexLibraries = await GetLibraries();
        var preferredPlexLibrary = Environment.GetEnvironmentVariable("PLEX_LIBRARY");
        PlexLibrary? plexLibrary = null;
        if (preferredPlexLibrary == null)
        {
            _logger.LogWarning("PLEX_LIBRARY not set; falling back to the first artist-type library.");
        }
        else if (plexLibraries.FirstOrDefault(it => string.Equals(it.Title, preferredPlexLibrary)) == null)
        {
            _logger.LogWarning(
                "Preferred Plex library {Library} not found; falling back to the first artist-type library.",
                preferredPlexLibrary);
        }
        else
        {
            plexLibrary = plexLibraries.First(it => string.Equals(it.Title, preferredPlexLibrary));
            _logger.LogInformation("Using preferred Plex library {Library}.", plexLibrary.Title);
        }

        if (plexLibrary == null)
        {
            plexLibrary = plexLibraries.Where(it => it.Type == "artist").Take(1).First();
            _logger.LogWarning("Fell back to artist-type Plex library {Library}.", plexLibrary.Title);
        }

        return plexLibrary;
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

public record PlexMusicAlbum
{
    public int RatingKey { get; set; }
    public string Title { get; set; }       // album title
    public string ParentTitle { get; set; } // owning artist's name
}