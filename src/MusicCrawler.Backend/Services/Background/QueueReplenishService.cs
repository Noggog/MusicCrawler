using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Background;

/// <summary>
/// Keeps every user's recommendation queue topped up: shortly after startup, then on a fixed cadence,
/// runs a gentle additive <see cref="IQueueReplenisher.TopUp"/> per user — growing the frontier and
/// refreshing stale similarity edges without disturbing pending order (decisions already expand the
/// frontier live, so this only covers staleness + slow drift). Per-user failures are logged and
/// skipped so one bad user doesn't abort the pass; a failed pass retries at the next interval.
/// </summary>
public class QueueReplenishService : BackgroundService
{
    private readonly IUserQueueRepo _queue;
    private readonly IQueueReplenisher _replenisher;
    private readonly ReplenishConfig _config;
    private readonly ILogger<QueueReplenishService> _logger;

    public QueueReplenishService(
        IUserQueueRepo queue,
        IQueueReplenisher replenisher,
        ReplenishConfig config,
        ILogger<QueueReplenishService> logger)
    {
        _queue = queue;
        _replenisher = replenisher;
        _config = config;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Observable
            .Timer(_config.StartupDelay, _config.Interval)
            .SelectMany(_ => Observable.FromAsync(ReplenishAll))
            .ToTask(stoppingToken);
    }

    /// <summary>Tops up every user once. Public so it can be unit-tested without the timer.</summary>
    public async Task ReplenishAll()
    {
        string[] userIds;
        try
        {
            userIds = await _queue.GetAllUserIds();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Queue replenish pass could not enumerate users; will retry next interval");
            return;
        }

        foreach (var userId in userIds)
        {
            try
            {
                await _replenisher.TopUp(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queue replenish failed for {User}; skipping to the next user", userId);
            }
        }
    }
}
