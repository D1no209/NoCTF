using Microsoft.Extensions.Time.Testing;
using NoCTF.Bot.Commands;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class SlidingWindowLimiterTests
{
    [Test]
    public async Task TryAcquire_LimitReached_RecoversAfterWindow()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-20T00:00:00Z"));
        var limiter = new SlidingWindowLimiter(time);

        await Assert.That(limiter.TryAcquire("user:1", 2, TimeSpan.FromSeconds(10))).IsTrue();
        await Assert.That(limiter.TryAcquire("user:1", 2, TimeSpan.FromSeconds(10))).IsTrue();
        await Assert.That(limiter.TryAcquire("user:1", 2, TimeSpan.FromSeconds(10))).IsFalse();

        time.Advance(TimeSpan.FromSeconds(10));

        await Assert.That(limiter.TryAcquire("user:1", 2, TimeSpan.FromSeconds(10))).IsTrue();
    }
}
