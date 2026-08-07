using Mycelium.Interfaces;

namespace Mycelium.Backend.Services.Singletons;

/// <summary>
/// The live automatic/manual switch for the download drainer. Owned entirely by the switch on the
/// Download page and persisted in Mongo, so it survives a redeploy — deliberately not an env var as
/// well, since a second source of truth could only contradict what the UI shows. Read through on every
/// check rather than cached at startup, so toggling takes effect on the next drainer tick instead of
/// needing a restart.
/// </summary>
public class DownloadSettings
{
    /// <summary>What a store that's never been toggled means: draining unattended is the normal mode.</summary>
    private const bool DefaultAutomatic = true;

    private readonly IAppSettingsRepo _repo;
    private readonly ILogger<DownloadSettings> _logger;

    public DownloadSettings(IAppSettingsRepo repo, ILogger<DownloadSettings> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>Whether the background drainer should enqueue on its own. Manual "download now" works
    /// either way — this governs only the unattended pass.</summary>
    public async Task<bool> Automatic() => await _repo.GetDownloadsAutomatic() ?? DefaultAutomatic;

    public async Task SetAutomatic(bool automatic)
    {
        await _repo.SetDownloadsAutomatic(automatic);
        _logger.LogInformation("Automatic downloads switched {State}", automatic ? "on" : "off");
    }
}
