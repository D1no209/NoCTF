using NoCTF.Application.Runtime;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class OrphanRuntimePolicyTests
{
    [Test]
    public async Task Prepared_placeholder_uses_configured_claim_grace()
    {
        var now = new DateTimeOffset(2026, 7, 20, 16, 0, 0, TimeSpan.Zero);
        var instance = new ChallengeInstance
        {
            Receipt = string.Empty,
            UpdatedAt = now.AddMinutes(-6)
        };

        var withinConfiguredLease = EfOrphanRuntimeStore.IsPreparedPlaceholderWithinLease(
            instance, now, TimeSpan.FromMinutes(2));
        var outsideShorterLease = EfOrphanRuntimeStore.IsPreparedPlaceholderWithinLease(
            instance, now, TimeSpan.FromSeconds(30));

        await Assert.That(withinConfiguredLease).IsTrue();
        await Assert.That(outsideShorterLease).IsFalse();
    }
}
