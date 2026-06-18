using Microsoft.Extensions.Logging.Abstractions;
using MusicCrawler.Backend;
using MusicCrawler.Backend.Services.Background;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using NSubstitute;
using Xunit;

namespace MusicCrawler.Tests;

public class QueueReplenishServiceTests
{
    private readonly IUserQueueRepo _queue = Substitute.For<IUserQueueRepo>();
    private readonly IQueueReplenisher _replenisher = Substitute.For<IQueueReplenisher>();

    private QueueReplenishService Build() => new(
        _queue,
        _replenisher,
        new ReplenishConfig(TimeSpan.FromHours(24), TimeSpan.FromMinutes(5)),
        NullLogger<QueueReplenishService>.Instance);

    [Fact]
    public async Task Tops_up_every_user_once()
    {
        _queue.GetAllUserIds().Returns(new[] { "u1", "u2", "u3" });

        await Build().ReplenishAll();

        await _replenisher.Received(1).TopUp("u1");
        await _replenisher.Received(1).TopUp("u2");
        await _replenisher.Received(1).TopUp("u3");
    }

    [Fact]
    public async Task One_failing_user_does_not_stop_the_rest()
    {
        _queue.GetAllUserIds().Returns(new[] { "u1", "u2", "u3" });
        _replenisher.TopUp("u2").Returns(Task.FromException(new InvalidOperationException("boom")));

        await Build().ReplenishAll();

        // u2 threw, but u1 and u3 are still topped up — the pass is resilient per-user.
        await _replenisher.Received(1).TopUp("u1");
        await _replenisher.Received(1).TopUp("u3");
    }
}
