using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Admission;
using NoCTF.Infrastructure.Admission;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Admission;

[Category("Integration"), NotInParallel]
public sealed class PersistedRequestAdmissionTests
{
    [Test, Timeout(180_000)]
    public async Task Independent_instances_share_atomic_quotas_and_renewable_slots(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_admission")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(
                    typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention().Options;
            await using (var db = new NoCtfDbContext(options))
                await db.Database.MigrateAsync(ct);
            var factory = new TestDbContextFactory(options);
            var first = new PersistedRequestAdmission(factory, TimeProvider.System);
            var second = new PersistedRequestAdmission(factory, TimeProvider.System);

            for (var i = 0; i < 20; i++)
                await using (await first.AcquireAsync(
                    [new("campus-ip", 100, 60), new($"account-{i}", 2, 60)], [], ct)) { }
            await using (await second.AcquireAsync(
                [new("different-ip", 100, 60), new("account-0", 2, 60)], [], ct)) { }
            await ExpectFailure(() => second.AcquireAsync(
                [new("rotated-ip", 100, 60), new("account-0", 2, 60)], [], ct),
                AdmissionFailure.RateLimited);

            await using (var lease = await first.AcquireAsync(
                [], [new("patch-global", 1), new("target-1", 1)], ct))
            {
                await ExpectFailure(() => second.AcquireAsync(
                    [], [new("patch-global", 1)], ct), AdmissionFailure.CapacityBusy);
                await Task.Delay(TimeSpan.FromSeconds(32), ct);
                await Assert.That(lease.Token.IsCancellationRequested).IsFalse();
                await ExpectFailure(() => second.AcquireAsync(
                    [], [new("patch-global", 1)], ct), AdmissionFailure.CapacityBusy);
            }
            await using (await second.AcquireAsync(
                [], [new("patch-global", 1)], ct)) { }

            var contenders = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                try
                {
                    await using var admission = await (index % 2 == 0 ? first : second)
                        .AcquireAsync([new("same-account-race", 1, 60)], [], ct);
                    return true;
                }
                catch (AdmissionRejectedException exception)
                    when (exception.Failure == AdmissionFailure.RateLimited)
                {
                    return false;
                }
            }));
            await Assert.That(contenders.Count(value => value)).IsEqualTo(1);

            await using var interrupted = await first.AcquireAsync(
                [], [new("in-flight", 1)], ct);
            await postgres.StopAsync(ct);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, interrupted.Token)
                    .WaitAsync(TimeSpan.FromSeconds(25), ct);
            }
            catch (OperationCanceledException) when (interrupted.Token.IsCancellationRequested) { }
            await Assert.That(interrupted.Token.IsCancellationRequested).IsTrue();
            await ExpectFailure(() => second.AcquireAsync([], [], ct),
                AdmissionFailure.DependencyUnavailable);
        });
    }

    private static async Task ExpectFailure(
        Func<ValueTask<IRequestAdmissionLease>> action, AdmissionFailure failure)
    {
        AdmissionRejectedException? rejected = null;
        try { await using var lease = await action(); }
        catch (AdmissionRejectedException exception) { rejected = exception; }
        await Assert.That(rejected).IsNotNull();
        await Assert.That(rejected!.Failure).IsEqualTo(failure);
        await Assert.That(rejected.RetryAfterSeconds).IsGreaterThan(0);
    }
}
