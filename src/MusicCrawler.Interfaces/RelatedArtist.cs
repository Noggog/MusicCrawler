namespace MusicCrawler.Interfaces;

/// <summary>One related/similar artist plus its image (when a source supplied one).</summary>
public record RelatedArtist(ArtistKey ArtistKey, string? ImageUrl);

/// <summary>
/// One edge set in the similarity graph: the related artists a single <paramref name="Source"/>
/// (e.g. "deezer") reported for <paramref name="Artist"/>, with the time it was fetched. Stored
/// per (artist, source) so each source has its own staleness and provenance is never lost.
/// </summary>
public record ArtistRelations(
    ArtistKey Artist,
    string Source,
    IReadOnlyList<RelatedArtist> Related,
    DateTimeOffset FetchedAt);

/// <summary>
/// A related artist after merging every source: one entry per distinct artist, carrying the
/// set of <paramref name="Sources"/> that recommended it (so the UI can show all the options).
/// </summary>
public record UnifiedRelatedArtist(ArtistKey ArtistKey, string? ImageUrl, IReadOnlyList<string> Sources);

/// <summary>The unified, cross-source view of related artists presented to the end user.</summary>
public record UnifiedRelations(ArtistKey Artist, IReadOnlyList<UnifiedRelatedArtist> Related);
