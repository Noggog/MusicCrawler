namespace Mycelium.Backend.Services.Singletons;

/// <summary>
/// When the drainer next intends to act — written by DownloadService as it settles in to wait, read
/// by the monitor snapshot so the Download page can count down instead of just saying "Idle".
/// Its own tiny singleton rather than a property on DownloadService, because the snapshot is built by
/// PurchaseService, which DownloadService already depends on — reading it back the other way would be
/// a dependency cycle.
///
/// Times are stored as UTC ticks read/written atomically (0 = nothing scheduled), since the writer is
/// the drainer loop and the reader is whichever request thread is polling the panel.
/// </summary>
public class DownloadSchedule
{
    private long _nextItemTicks;
    private long _nextBatchTicks;

    /// <summary>When the wait between two albums ends, or null when nothing is spacing out right now
    /// (idle, or an album is actively downloading).</summary>
    public DateTimeOffset? NextItemAt => Read(ref _nextItemTicks);

    /// <summary>When the automatic pass next looks for pending albums. Set even in manual mode — the
    /// pass still runs, it just does nothing — so the UI only shows it when automatic is on.</summary>
    public DateTimeOffset? NextBatchAt => Read(ref _nextBatchTicks);

    public void ItemWait(TimeSpan delay) => Write(ref _nextItemTicks, delay);

    /// <summary>The inter-album wait is over (or was never entered) — nothing pending item-wise.</summary>
    public void ClearItemWait() => Interlocked.Exchange(ref _nextItemTicks, 0);

    public void BatchWait(TimeSpan delay) => Write(ref _nextBatchTicks, delay);

    private static void Write(ref long field, TimeSpan delay) =>
        Interlocked.Exchange(
            ref field, delay <= TimeSpan.Zero ? 0 : (DateTimeOffset.UtcNow + delay).UtcTicks);

    private static DateTimeOffset? Read(ref long field) =>
        Interlocked.Read(ref field) is var ticks && ticks == 0
            ? null
            : new DateTimeOffset(ticks, TimeSpan.Zero);
}
