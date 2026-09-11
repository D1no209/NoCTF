using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Common;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AtomicAggregatePatchPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Later_section_rejection_rolls_back_earlier_saved_sections(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_atomic_aggregate_patch")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;

            await using (var setup = new NoCtfDbContext(options))
                await setup.Database.EnsureCreatedAsync(cancellationToken);

            await using (var db = new NoCtfDbContext(options))
            {
                IAtomicAggregatePatch patch = new AggregatePatchTransaction(
                    db,
                    new NoOpTransactionalMessageOutbox());
                var outcome = await patch.ExecuteAsync(async ct =>
                {
                    await using var sectionTransaction =
                        await AggregateCompatibleTransaction.BeginAsync(db, ct);
                    var settings = await db.PlatformSettings.SingleAsync(ct);
                    settings.Name = "must roll back";
                    await db.SaveChangesAsync(ct);
                    await sectionTransaction.CommitAsync(ct);
                    return AtomicAggregatePatchDecision<string>.Rollback("rejected");
                }, cancellationToken);

                await Assert.That(outcome).IsEqualTo("rejected");
                await Assert.That(db.ChangeTracker.Entries()).IsEmpty();
            }

            await using (var verification = new NoCtfDbContext(options))
            {
                var settings = await verification.PlatformSettings.AsNoTracking()
                    .SingleAsync(cancellationToken);
                await Assert.That(settings.Name).IsEqualTo("NoCTF");
            }
        });
    }
}
