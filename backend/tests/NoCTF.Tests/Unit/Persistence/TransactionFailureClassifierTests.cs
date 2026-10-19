using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;
using Npgsql;

namespace NoCTF.Tests.Unit.Persistence;

public sealed class TransactionFailureClassifierTests
{
    [Test]
    public async Task Only_known_transaction_conflicts_are_retryable()
    {
        foreach (var state in new[] { "40001", "40P01" })
            await Assert.That(TransactionFailureClassifier.IsRetryable(Wrapped(state))).IsTrue();
        foreach (var state in new[] { "53300", "57014", "08006", "23505", "23503" })
            await Assert.That(TransactionFailureClassifier.IsRetryable(Wrapped(state))).IsFalse();
        await Assert.That(TransactionFailureClassifier.IsRetryable(
            new DbUpdateConcurrencyException("Stale concurrency stamp"))).IsTrue();
    }

    [Test]
    public async Task Dependency_failures_are_distinct_from_constraints_and_conflicts()
    {
        foreach (var state in new[] { "53300", "57014", "08006", "57P01" })
            await Assert.That(TransactionFailureClassifier.IsDependencyUnavailable(Wrapped(state))).IsTrue();
        foreach (var state in new[] { "23505", "23503", "40001", "40P01" })
            await Assert.That(TransactionFailureClassifier.IsDependencyUnavailable(Wrapped(state))).IsFalse();
        await Assert.That(TransactionFailureClassifier.IsDependencyUnavailable(
            new InvalidOperationException("Connection wait", new TimeoutException()))).IsTrue();
    }

    private static Exception Wrapped(string state) => new InvalidOperationException("EF wrapper",
        new DbUpdateException("Save failed", new PostgresException("Database failure", "ERROR", "ERROR", state)));
}
