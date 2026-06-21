using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace MusicCrawler.Plex.Services.Singletons;

public class PlexApi : IPlexApi
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
        // includeCollections=1 so each artist's Collection tags come back inline (Plex omits them from
        // the bare listing); the per-user like/dislike tagging reads these to merge writes additively.
        string url = $"{_endpointInfo.BaseUri}/library/sections/{library}/all?includeCollections=1";
        _logger.LogDebug("Plex GetMusicArtists from library {Library}: {Url}", library, url);
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        return data["MediaContainer"]["Metadata"].ToObject<PlexMusicArtist[]>();
    }

    /// <summary>
    /// Fetches a single artist by its rating key (the targeted GET mirror of the whole-section
    /// listing). Returns <c>null</c> when the key no longer resolves (e.g. the item was removed or the
    /// library was rebuilt and keys shifted), so callers can fall back to a name scan. The
    /// <c>/library/metadata/{ratingKey}</c> response carries the same inline <c>Collection</c> array as
    /// the section listing (with includeCollections=1), so the tagger can merge the artist's current
    /// collections without a full scan.
    /// </summary>
    public async Task<PlexMusicArtist?> GetMusicArtist(int ratingKey)
    {
        var url = $"{_endpointInfo.BaseUri}/library/metadata/{ratingKey}?includeCollections=1";
        _logger.LogDebug("Plex GetMusicArtist {RatingKey}: {Url}", ratingKey, url);
        var response = await httpClient.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        return (data["MediaContainer"]?["Metadata"] as JArray)?.FirstOrDefault()?.ToObject<PlexMusicArtist>();
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
    /// Replaces an artist's full Collection set in section <paramref name="library"/>. Plex's metadata
    /// edit is a whole-field replace, so callers must merge in the existing collections first (see
    /// <see cref="PlexMusicArtist.Collections"/>). <c>type=8</c> is the artist metadata type;
    /// <c>collection.locked=1</c> pins the field so a later metadata refresh won't drop the tags.
    /// </summary>
    public async Task SetArtistCollections(int library, int ratingKey, IReadOnlyList<string> collections)
    {
        var url = new StringBuilder(
            $"{_endpointInfo.BaseUri}/library/sections/{library}/all?type=8&id={ratingKey}");
        for (var i = 0; i < collections.Count; i++)
        {
            url.Append($"&collection[{i}].tag.tag={Uri.EscapeDataString(collections[i])}");
        }
        url.Append("&collection.locked=1");

        _logger.LogDebug(
            "Plex SetArtistCollections for {RatingKey}: {Count} collection(s)", ratingKey, collections.Count);
        var response = await httpClient.PutAsync(url.ToString(), content: null);
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

    // Plex returns genre tags inline on the section listing, e.g. "Genre":[{"tag":"Pop/Rock"}].
    public PlexTag[]? Genre { get; set; }

    // Collection memberships. Returned inline on the section listing when includeCollections=1 is set
    // (see GetMusicArtists), in a field separate from Genre/Mood/Style — so editing collections never
    // disturbs those. The per-user like/dislike tags live here because, unlike Label, "Artist Collection"
    // is a field Plex music smart playlists can actually filter on.
    public PlexTag[]? Collection { get; set; }

    /// <summary>The artist's current collection names; empty when it belongs to none.</summary>
    public string[] Collections() =>
        Collection?.Select(t => t.Tag).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>();
}

/// <summary>A Plex tag entry — genres, moods, styles all serialize as <c>{ "tag": "..." }</c>.</summary>
public record PlexTag
{
    public string Tag { get; set; }
}

public record PlexMusicAlbum
{
    public int RatingKey { get; set; }
    public string Title { get; set; }       // album title
    public string ParentTitle { get; set; } // owning artist's name
}