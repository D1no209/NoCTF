using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RuntimeExtensionPolicyTests
{
    [Test]
    public async Task CalculateExpiry_ExtendsFromExistingExpiryEvenWithTimeRemaining()
    {
        var now = DateTimeOffset.Parse("2026-09-28T00:00:00Z");

        var result = RuntimeExtensionPolicy.CalculateExpiry(
            now.AddMinutes(52), now, TimeSpan.FromMinutes(30));

        await Assert.That(result).IsEqualTo(now.AddMinutes(82));
    }

    [Test]
    public async Task CalculateExpiry_ExtendsNearExpiryWithoutDiscardingRemainingTime()
    {
        var now = DateTimeOffset.Parse("2026-09-28T00:00:00Z");

        var result = RuntimeExtensionPolicy.CalculateExpiry(
            now.AddMinutes(9), now, TimeSpan.FromMinutes(30));

        await Assert.That(result).IsEqualTo(now.AddMinutes(39));
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    public async Task CalculateExpiry_RejectsNonPositiveExtension(int minutes)
    {
        var now = DateTimeOffset.Parse("2026-09-28T00:00:00Z");

        var result = RuntimeExtensionPolicy.CalculateExpiry(
            now.AddMinutes(52), now, TimeSpan.FromMinutes(minutes));

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task CalculateExpiry_RejectsExpiredRuntimeAndOverflow()
    {
        var now = DateTimeOffset.Parse("2026-09-28T00:00:00Z");

        await Assert.That(RuntimeExtensionPolicy.CalculateExpiry(
            now, now, TimeSpan.FromMinutes(30))).IsNull();
        await Assert.That(RuntimeExtensionPolicy.CalculateExpiry(
            DateTimeOffset.MaxValue.AddMinutes(-1), now,
            TimeSpan.FromMinutes(2))).IsNull();
    }
}
