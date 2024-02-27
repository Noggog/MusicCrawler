using MusicCrawler.Spotify.Models;

namespace MusicCrawler.Spotify.Services.Singletons;

public interface ISpotifyApi
{
    /**
     * responds with an html for a page where the user can click "accept".
     * Might be useful if we want to access information specific to a spotify user.
     */
    Task<string> OAuthLogin();

    /**
     * retrieves an oAuth token without user input and therefore has no user-related permissions.
     */
    Task<string> NonUserOAuthToken();

    /**
     * [seed_artists] example: 4NHQUGzhtTLFvgF5SZesLK
     */
    Task<RecommendedArtistsDto> Recommendations(string token, string seedArtists);

    /**
     * Useful for retrieving an artistId from an artistName.
     * 
     * [artistName] example: Genghis Tron
     */
    Task<SearchArtistDto> SearchArtist(string token, string artistName);
}