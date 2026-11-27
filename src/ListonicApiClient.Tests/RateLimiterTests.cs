using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace ListonicApiClient.Tests;

public class RateLimiterTests
{
    [Test]
    public async Task Defaults_Are15Burst_HalfSecondPeriod()
    {
        var defaults = new ListonicRateLimitOptions();
        await Assert.That(defaults.TokenLimit).IsEqualTo(15);
        await Assert.That(defaults.ReplenishmentPeriod).IsEqualTo(TimeSpan.FromMilliseconds(500));
    }

    [Test]
    public async Task Burst_UpToTokenLimit_IsImmediate_ThenQueues()
    {
        var options = new ListonicRateLimitOptions();
        using var limiter = new ListonicRateLimiter(Options.Create(options));

        for (var i = 0; i < options.TokenLimit; i++)
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
        await Assert.That(sw.Elapsed).IsLessThan(options.ReplenishmentPeriod * 4);
    }

    [Test]
    public async Task ConfiguredBurst_IsHonoured()
    {
        using var limiter = new ListonicRateLimiter(Options.Create(new ListonicRateLimitOptions { TokenLimit = 2 }));
        using var a = limiter.Limiter.AttemptAcquire();
        using var b = limiter.Limiter.AttemptAcquire();
        using var c = limiter.Limiter.AttemptAcquire();
        await Assert.That(b.IsAcquired).IsTrue();
        await Assert.That(c.IsAcquired).IsFalse();
    }

    [Test]
    [Arguments(0, 500)]
    [Arguments(1001, 500)]
    [Arguments(15, 0)]
    [Arguments(15, 120_000)]
    public async Task Validator_RejectsNonsense(int tokenLimit, int periodMs) =>
        await Assert.That(new ListonicRateLimitOptionsValidator().Validate(null, new ListonicRateLimitOptions
        {
            TokenLimit = tokenLimit, ReplenishmentPeriod = TimeSpan.FromMilliseconds(periodMs),
        }).Failed).IsTrue();

    [Test]
    public async Task Validator_AcceptsDefaults() =>
        await Assert.That(new ListonicRateLimitOptionsValidator().Validate(null, new ListonicRateLimitOptions()).Succeeded).IsTrue();
}
