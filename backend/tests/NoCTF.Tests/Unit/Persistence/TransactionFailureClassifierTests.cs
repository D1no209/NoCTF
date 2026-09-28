using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Persistence;

public sealed class TransactionFailureClassifierTests
{
    [Test]
    public async Task Wrapped_provider_transient_failure_is_retryable_without_provider_error_codes()
    {
        var failure = new InvalidOperationException("EF execution strategy wrapper",
            new DbUpdateException("Save failed", new FakeDbException(true)));

        await Assert.That(TransactionFailureClassifier.IsRetryable(failure)).IsTrue();
        await Assert.That(TransactionFailureClassifier.IsRetryable(
            new DbUpdateException("Constraint violation", new FakeDbException(false)))).IsFalse();
    }

    [Test]
    public async Task Optimistic_concurrency_conflict_is_retryable()
    {
        await Assert.That(TransactionFailureClassifier.IsRetryable(
            new DbUpdateConcurrencyException("Stale concurrency stamp"))).IsTrue();
    }

    private sealed class FakeDbException(bool transient) : DbException("Test provider failure")
    {
        public override bool IsTransient => transient;
    }
}
