namespace MusicCrawler.Deezer.Inputs;

/// <summary>
/// Base URI for the Deezer public API. Deezer is keyless, so this is the only knob — defaulted to
/// the public endpoint and overridable via the <c>DEEZER_BASE_URI</c> env var (see DeezerModule).
/// </summary>
public record DeezerEndpointInfo(string BaseUri);
