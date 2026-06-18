namespace MusicCrawler.Interfaces;

/// <summary>
/// The seam to an external acquisition backend (e.g. Lidarr). Phase 5 ships a no-op implementation;
/// a real target is plugged in later behind this interface without touching the purchase list or its
/// UI. The Library refresh closes the loop — once an ordered item appears in Plex its purchase row
/// flips to <see cref="PurchaseStatus.InLibrary"/> and drops off the list.
/// </summary>
public interface IDownloader
{
    /// <summary>A human-readable name for the active backend, surfaced in logs/UI.</summary>
    string Name { get; }

    /// <summary>
    /// Requests acquisition of one purchase item. Returns true if the backend accepted it (the caller
    /// then advances the item to <see cref="PurchaseStatus.Sent"/>). The no-op stub logs and accepts,
    /// so the manual "mark ordered" flow works today.
    /// </summary>
    Task<bool> Request(PurchaseItem item);
}
