using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeOperationClaimStoreTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Stale_claim_is_single_owner_and_fences_old_owner(
        CancellationToken cancellationToken)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_runtime_claims")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.CreateVersion7(now);
        await using (var setup = new NoCtfDbContext(options))
        {
            await setup.Database.MigrateAsync(cancellationToken);
            setup.Competitions.Add(new Competition
            {
                Id = competitionId,
                Title = "Runtime recovery",
                OwnerId = Guid.NewGuid(),
                Mode = GameMode.Ctf,
                StartTime = now.AddHours(-1),
                EndTime = now.AddHours(1),
                Status = CompetitionStatus.Running,
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync(cancellationToken);
        }

        RuntimeOperationLease originalLease;
        await using (var firstContext = new NoCtfDbContext(options))
        {
            var first = await new EfRuntimeOperationStore(firstContext).BeginAsync(
                competitionId,
                "runtime:recovery",
                RuntimeOperationKind.CreateContainer,
                now.AddMinutes(-6),
                now,
                cancellationToken);
            originalLease = first.Lease!;
        }

        await using (var ageContext = new NoCtfDbContext(options))
        {
            await ageContext.RuntimeOperations.ExecuteUpdateAsync(
                update => update.SetProperty(operation => operation.UpdatedAt, now.AddMinutes(-10)),
                cancellationToken);
        }

        var claims = await Task.WhenAll(
            ClaimAsync(options, competitionId, now.AddMinutes(1), cancellationToken),
            ClaimAsync(options, competitionId, now.AddMinutes(1), cancellationToken));
        await Assert.That(claims.Count(result => result.Lease!.IsNew)).IsEqualTo(1);
        var winningLease = claims.Single(result => result.Lease!.IsNew).Lease!;
        await Assert.That(winningLease.ClaimToken).IsNotEqualTo(originalLease.ClaimToken);

        var oldReceipt = Receipt(originalLease.ClaimToken, "old-resource");
        await using (var staleContext = new NoCtfDbContext(options))
        {
            var completed = await new EfRuntimeOperationStore(staleContext).CompleteAsync(
                originalLease, oldReceipt, Guid.NewGuid(), null, null, now, cancellationToken);
            await Assert.That(completed).IsFalse();
        }

        var winnerReceipt = Receipt(winningLease.ClaimToken, "winner-resource");
        await using (var winnerContext = new NoCtfDbContext(options))
        {
            var completed = await new EfRuntimeOperationStore(winnerContext).CompleteAsync(
                winningLease, winnerReceipt, Guid.NewGuid(), null, null, now, cancellationToken);
            await Assert.That(completed).IsTrue();
        }

        await using (var staleFailureContext = new NoCtfDbContext(options))
        {
            _ = await new EfRuntimeOperationStore(staleFailureContext).FailAsync(
                originalLease,
                new(RuntimeProvisionFailure.PersistenceRejected, Guid.NewGuid(), null, null, oldReceipt),
                now,
                cancellationToken);
        }

        await using var verify = new NoCtfDbContext(options);
        var operation = await verify.RuntimeOperations.AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(operation.Status).IsEqualTo(RuntimeStatus.Running);
        await Assert.That(operation.ClaimToken).IsEqualTo(winningLease.ClaimToken);
        var instances = await verify.ChallengeInstances.AsNoTracking().ToListAsync(cancellationToken);
        await Assert.That(instances.Count).IsEqualTo(2);
        await Assert.That(instances.Count(instance => instance.Status == RuntimeStatus.Failed)).IsEqualTo(1);
    }

    private static async Task<RuntimeOperationBeginResult> ClaimAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new EfRuntimeOperationStore(db).BeginAsync(
            competitionId,
            "runtime:recovery",
            RuntimeOperationKind.CreateContainer,
            now.AddMinutes(-6),
            now,
            cancellationToken);
    }

    private static ContainerReceipt Receipt(Guid claimToken, string resourceId) => new(
        claimToken,
        RuntimeProvider.Docker,
        resourceId,
        RuntimeStatus.Running,
        new Dictionary<int, int>(),
        "localhost",
        null);
}
