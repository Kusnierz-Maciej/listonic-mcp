using System.Diagnostics;

namespace ListonicApiClient.Tests;

public class RateLimiterTests
{
    [Test]
    public async Task Burst_UpToTokenLimit_IsImmediate_ThenQueues()
    {
        using var limiter = new ListonicRateLimiter();

        for (var i = 0; i < ListonicRateLimiter.TokenLimit; i++)
        {
            using var lease = limiter.Limiter.AttemptAcquire();
            await Assert.That(lease.IsAcquired).IsTrue();
        }

        // Bucket empty: an immediate attempt fails, but an awaited acquire queues until a token refills.
        using (var rejected = limiter.Limiter.AttemptAcquire())
            await Assert.That(rejected.IsAcquired).IsFalse();

        var sw = Stopwatch.StartNew();
        using var queued = await limiter.Limiter.AcquireAsync();
        sw.Stop();

        await Assert.That(queued.IsAcquired).IsTrue();
        await Assert.That(sw.Elapsed).IsLessThan(ListonicRateLimiter.ReplenishmentPeriod * 4);
    }
}
