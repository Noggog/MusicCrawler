using MusicCrawler.Interfaces;
using MusicCrawler.Plex.Services;
using MusicCrawler.Plex.Services.Singletons;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// <see cref="IArtistTagger"/> over Plex. Stamps a user's like/dislike onto the artist in Plex as a
/// Label (e.g. "noggog_liked"), so a taste verdict made in the app is visible and queryable in Plex
/// itself.
///
/// <para><b>Additive.</b> Plex's metadata edit replaces the whole Label field, so we read the artist's
/// current labels and PUT them back plus the new one — preserving genres (a separate field) and any
/// other user's tags. A name can map to more than one Plex item (Plex joins collaborators into a single
/// ';'-delimited title), so every item the name appears in is tagged, matching how the rest of the app
/// reads names.</para>
///
/// <para><b>Best-effort.</b> Failures are logged, never thrown — tagging is a side effect of rating and
/// must not fail the rating itself. (Removing the opposite verdict's tag on a flip is a deliberate
/// follow-up; today this only ever adds.)</para>
/// </summary>
public class PlexArtistTagger : IArtistTagger
{
    private readonly PlexApi _plexApi;
    private readonly ILogger<PlexArtistTagger> _logger;

    public PlexArtistTagger(PlexApi plexApi, ILogger<PlexArtistTagger> logger)
    {
        _plexApi = plexApi;
        _logger = logger;
    }

    public async Task AddTag(string artistName, string tag)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        try
        {
            var library = await _plexApi.ResolveLibrary();
            var matches = (await _plexApi.GetMusicArtists(library.Key))
                .Where(a => ArtistNames.Split(a.Title)
                    .Any(n => string.Equals(n, artistName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (matches.Length == 0)
            {
                _logger.LogInformation("No Plex artist matched {Artist}; skipped tag {Tag}", artistName, tag);
                return;
            }

            foreach (var artist in matches)
            {
                var existing = artist.Labels();
                if (existing.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                await _plexApi.SetArtistLabels(library.Key, artist.RatingKey, existing.Append(tag).ToArray());
                _logger.LogInformation(
                    "Tagged Plex artist {Artist} ({Key}) with {Tag}", artist.Title, artist.RatingKey, tag);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to tag Plex artist {Artist} with {Tag}", artistName, tag);
        }
    }
}
