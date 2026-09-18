using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Messages;
using NoCTF.Tests.Integration.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeWriteBackOrderingTests
{
    [Test, Arguments(RuntimeState.Running), Arguments(RuntimeState.Stopped), Timeout(300_000)]
    public async Task Late_provision_results_do_not_revert_a_completed_transition(RuntimeState state, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options);
            await db.Database.EnsureCreatedAsync(ct);
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(db, ct);
            var runtime = await db.RuntimeInstances.SingleAsync(row => row.Id == fixture.RuntimeIds[0], ct);
            runtime.State = state;
            runtime.RunnerId = "runner";
            runtime.RunningAt = fixture.Now;
            runtime.ProviderReceiptJson = "{}";
            await db.SaveChangesAsync(ct);
            await db.Entry(runtime).ReloadAsync(ct);
            var runningAt = runtime.RunningAt;
            var before = await db.CompetitionEvents.CountAsync(ct);
            var handler = new RuntimeProvisionWriteBackMessageHandler(db, new NoOpTransactionalMessageOutbox(),
                NullCompetitionEventRecorder.Instance, TimeProvider.System);
            await handler.Handle(new RuntimeProvisionFailed(runtime.Id, RuntimeFailureCode.RunnerUnavailable, "runner"), ct);
            await handler.Handle(new RuntimeProvisioned(runtime.Id, "runner", RuntimeProvider.Docker, "{\"late\":true}", [], null), ct);
            await db.Entry(runtime).ReloadAsync(ct);
            await Assert.That(runtime.State).IsEqualTo(state);
            await Assert.That(runtime.FailureCode).IsNull();
            await Assert.That(runtime.RunningAt).IsEqualTo(runningAt);
            await Assert.That(runtime.ProviderReceiptJson).IsEqualTo("{}");
            await Assert.That(await db.CompetitionEvents.CountAsync(ct)).IsEqualTo(before);
        });
    }
}
