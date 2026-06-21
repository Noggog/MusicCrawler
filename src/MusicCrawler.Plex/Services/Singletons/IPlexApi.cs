namespace MusicCrawler.Plex.Services.Singletons;

/// <summary>
/// The Plex HTTP surface used by the app. Extracted so Plex-touching orchestration (notably the
/// artist tagger's stored-key vs. full-scan paths) can be unit-tested against a mock instead of a
/// live server. <see cref="PlexApi"/> is the only implementation; the <c>PlexModule</c> assembly
/// scan exposes it as this interface and as itself (the same singleton).
/// </summary>
public interface IPlexApi
{
    Task<PlexLibrary[]> GetLibraries();
    Task<PlexMusicArtist[]> GetMusicArtists(int library);

    /// <summary>One artist by rating key, or <c>null</c> when the key no longer resolves.</summary>
    Task<PlexMusicArtist?> GetMusicArtist(int ratingKey);

    Task<PlexMusicAlbum[]> GetMusicAlbums(int library);
    Task<PlexRecentlyAddedItem[]> GetRecentlyAdded(int libraryKey, int maxResults = 5);
    Task RefreshLibrary(int libraryKey);
    Task SetArtistCollections(
        int library, int ratingKey, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove);
    Task<PlexLibrary> ResolveLibrary();

    /// <summary>
    /// The server's stable <c>machineIdentifier</c> (the id app.plex.tv deep links are keyed by), or
    /// <c>null</c> if the server is unreachable. Cached after the first successful fetch.
    /// </summary>
    Task<string?> GetMachineIdentifier();
}
