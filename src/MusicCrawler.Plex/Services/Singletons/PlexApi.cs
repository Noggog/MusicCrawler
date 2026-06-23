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

    // The server's machineIdentifier is immutable; fetched once and cached for the process lifetime.
    private string? _machineIdentifier;

    public PlexApi(PlexEndpointInfo endpointInfo, PlexClientInfo clientInfo, ILogger<PlexApi> logger)
    {
        _endpointInfo = endpointInfo;
        _clientInfo = clientInfo;
        _logger = logger;
        this.httpClient = new HttpClient();
        this.httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        this.httpClient.DefaultRequestHeaders.Add("X-Plex-Token", clientInfo.Token);
    }

    public async Task<string?> GetMachineIdentifier()
    {
        if (_machineIdentifier != null)
        {
            return _machineIdentifier;
        }

        // The root endpoint's MediaContainer carries the server identity, including machineIdentifier.
        var url = $"{_endpointInfo.BaseUri}/";
        _logger.LogDebug("Plex GetMachineIdentifier: {Url}", url);
        var response = await httpClient.GetStringAsync(url);
        var data = JObject.Parse(response);
        var id = data["MediaContainer"]?["machineIdentifier"]?.ToString();
        // Only cache a real answer so a transient failure doesn't pin a null for the process lifetime.
        if (!string.IsNullOrEmpty(id))
        {
            _machineIdentifier = id;
        }
        return _machineIdentifier;
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
    ///
    /// <para>Unlike the section listing — which serializes <c>Guid</c> as a single string — the detail
    /// endpoint returns it as an <em>array</em> of external-id objects (mbid/etc.). That shape collides
    /// with <see cref="PlexMusicArtist.Guid"/> (a string), so we drop the field before deserializing; the
    /// tagger only needs RatingKey/Title/Collection and nothing reads Guid here.</para>
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
        if (data["MediaContainer"]?["Metadata"] is not JArray metadata
            || metadata.FirstOrDefault() is not JObject item)
        {
            return null;
        }

        // The detail endpoint serializes Guid as an array; PlexMusicArtist.Guid is a string. It's unused
        // on this path, so remove it rather than fight the type mismatch.
        item.Remove("Guid");
        return item.ToObject<PlexMusicArtist>();
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

    /// <summary>
    /// All tracks under an artist via <c>/library/metadata/{ratingKey}/allLeaves</c> — Plex flattens the
    /// artist's albums into one track list. Each track carries <c>userRating</c> (0–10, the token
    /// account's rating; absent when unrated). Returns empty when the key 404s (item removed / keys
    /// shifted on a rebuild) so the rating summary degrades to "no stats" rather than throwing.
    /// </summary>
    public async Task<PlexTrack[]> GetArtistTracks(int ratingKey)
    {
        var url = $"{_endpointInfo.BaseUri}/library/metadata/{ratingKey}/allLeaves";
        _logger.LogDebug("Plex GetArtistTracks {RatingKey}: {Url}", ratingKey, url);
        var response = await httpClient.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<PlexTrack>();
        }

        response.EnsureSuccessStatusCode();
        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var metadata = data["MediaContainer"]?["Metadata"];
        return metadata?.ToObject<PlexTrack[]>() ?? Array.Empty<PlexTrack>();
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
    /// Edits an artist's Collection set in section <paramref name="library"/>: adds every tag in
    /// <paramref name="add"/> and removes every tag in <paramref name="remove"/> in a single edit.
    /// Plex's tag edit is <b>not</b> a whole-field replace — listing <c>collection[i].tag.tag</c> only
    /// adds, and a tag is dropped only via the explicit <c>collection[].tag.tag-</c> parameter — so
    /// callers pass the delta, not the desired final set. Removed tags must be spelled exactly as Plex
    /// stores them (case included), so read them off the current item. <c>type=8</c> is the artist
    /// metadata type; <c>collection.locked=1</c> pins the field so a later refresh won't drop the tags.
    /// </summary>
    public async Task SetArtistCollections(
        int library, int ratingKey, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove)
    {
        if (add.Count == 0 && remove.Count == 0)
        {
            return;
        }

        var url = new StringBuilder(
            $"{_endpointInfo.BaseUri}/library/sections/{library}/all?type=8&id={ratingKey}");
        var i = 0;
        foreach (var tag in add)
        {
            url.Append($"&collection[{i}].tag.tag={Uri.EscapeDataString(tag)}");
            i++;
        }
        if (remove.Count > 0)
        {
            // Plex drops tags only via the "-" suffix param: a comma-separated list, each value escaped.
            var dropped = string.Join(",", remove.Select(Uri.EscapeDataString));
            url.Append($"&collection[].tag.tag-={dropped}");
        }
        url.Append("&collection.locked=1");

        _logger.LogDebug(
            "Plex SetArtistCollections for {RatingKey}: +{Add} -{Remove}", ratingKey, add.Count, remove.Count);
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

/// <summary>
/// A track ("leaf") returned by <c>allLeaves</c>. <see cref="UserRating"/> is the token account's rating
/// on Plex's 0–10 scale (10 = five stars) and is <c>null</c> for an unrated track — only rated tracks
/// feed the artist rating summary.
/// </summary>
public record PlexTrack
{
    public string Title { get; set; }
    public double? UserRating { get; set; }
}