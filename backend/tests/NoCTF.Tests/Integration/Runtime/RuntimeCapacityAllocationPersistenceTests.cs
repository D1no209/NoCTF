using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeCapacityAllocationPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Generated_migration_applies_and_typed_allocations_round_trip(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(ct);
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(db, ct);
            var runtime = await db.RuntimeInstances.SingleAsync(x => x.Id == fixture.RuntimeIds[0], ct);
            await Assert.That(runtime.CapacityAllocations.Items).IsEmpty();
            var allocation = new RuntimeCapacityAllocation(
                new(RuntimeWorkloadKind.VerificationTarget, runtime.Id, runtime.Id), runtime.GameplayFactId,
                "docker-domain", "runner", new(1024, 250, 128), new(1024, 500, 128));
            runtime.CapacityAllocations = runtime.CapacityAllocations.Add(allocation);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            runtime = await db.RuntimeInstances.SingleAsync(x => x.Id == fixture.RuntimeIds[0], ct);
            await Assert.That(runtime.CapacityAllocations.Items).IsEquivalentTo([allocation]);
            runtime.CapacityAllocations = runtime.CapacityAllocations.Remove(allocation.Identity);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            await Assert.That((await db.RuntimeInstances.SingleAsync(x => x.Id == fixture.RuntimeIds[0], ct))
                .CapacityAllocations.Items).IsEmpty();
        });
    }
}
