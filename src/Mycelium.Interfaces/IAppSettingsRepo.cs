namespace Mycelium.Interfaces;

/// <summary>
/// Durable operator settings — the few switches flipped from the UI rather than baked into the
/// environment, stored so they survive a redeploy. Each getter returns null when the setting has
/// never been set, in which case the caller applies its own default.
/// </summary>
public interface IAppSettingsRepo
{
    /// <summary>
    /// Whether the background download drainer enqueues pending albums on its own, or null when it
    /// has never been toggled (the caller then applies its own default).
    /// </summary>
    Task<bool?> GetDownloadsAutomatic();

    /// <summary>Persists the drainer switch, replacing the default from then on.</summary>
    Task SetDownloadsAutomatic(bool automatic);
}
