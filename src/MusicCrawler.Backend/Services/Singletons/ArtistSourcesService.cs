using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Builds the cross-source identity view for the Artists-page "Sources" tab: one row per correctable
/// source (from the registered <see cref="ISourceIdentityCorrector"/>s), each in a fixed display
/// order, with an unresolved placeholder when a source has no id yet. Appends a read-only ListenBrainz
/// row derived from the MusicBrainz MBID, since ListenBrainz has no identity of its own.
/// </summary>
public class ArtistSourcesService
{
    // Sources render in this order; any future corrector not listed falls to the end.
    private static readonly string[] DisplayOrder = { "deezer", "musicbrainz" };

    private readonly IReadOnlyList<ISourceIdentityCorrector> _correctors;

    public ArtistSourcesService(IEnumerable<ISourceIdentityCorrector> correctors)
    {
        _correctors = correctors
            .OrderBy(c => Array.IndexOf(DisplayOrder, c.Source) is var i && i < 0 ? int.MaxValue : i)
            .ToArray();
    }

    public async Task<ArtistSources> Get(ArtistKey artist)
    {
        var rows = new List<SourceIdentity>();

        foreach (var corrector in _correctors)
        {
            var current = await corrector.GetCurrent(artist)
                          ?? Unresolved(corrector.Source);
            rows.Add(current);

            // ListenBrainz keys off the MusicBrainz MBID — surface it as a read-only link out when
            // the MBID is known, right after the MusicBrainz row.
            if (corrector.Source == "musicbrainz" && current.Id is { Length: > 0 } mbid)
            {
                rows.Add(new SourceIdentity(
                    Source: "listenbrainz",
                    Id: mbid,
                    Name: current.Name,
                    Detail: "via MusicBrainz ID",
                    Link: $"https://listenbrainz.org/artist/{mbid}",
                    ImageUrl: null,
                    IsOverride: false,
                    Correctable: false));
            }
        }

        return new ArtistSources(artist, rows);
    }

    private static SourceIdentity Unresolved(string source) =>
        new(source, Id: null, Name: null, Detail: null, Link: null, ImageUrl: null,
            IsOverride: false, Correctable: true);
}
