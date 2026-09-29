using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RuntimeExtensionPolicyTests
{
    [Test]
    public async Task CalculateExpiry_RejectsBeforeFinalTenMinutes()
    {
        var now = DateTimeOffset.Parse("2026-09-28T00:00:00Z");

        var result = RuntimeExtensionPolicy.CalculateExpiry(
            now.AddMinutes(52), now, TimeSpan.FromMinutes(30));

        await Assert.That(result).IsNull();
        await Assert.That(RuntimeExtensionPolicy.IsWithinRenewalWindow(
            now.AddMinutes(10).AddTicks(1), now)).IsFalse();
    }

    [Test]
    public async Task CalculateExpiry_AllowsExactlyTenMinutesRemaining()
    {
        var now = DateTimeOffset.Parse("2026-09-28T00:00:00Z");

        var result = RuntimeExtensionPolicy.CalculateExpiry(
            now.AddMinutes(10), now, TimeSpan.FromMinutes(30));

        await Assert.That(result).IsEqualTo(now.AddMinutes(40));
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
        var nearMaximum = DateTimeOffset.MaxValue.AddMinutes(-1);
        await Assert.That(RuntimeExtensionPolicy.CalculateExpiry(
            nearMaximum, nearMaximum.AddMinutes(-9),
            TimeSpan.FromMinutes(2))).IsNull();
    }
}
