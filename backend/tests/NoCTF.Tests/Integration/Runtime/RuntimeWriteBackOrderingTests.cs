using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Messages;
using NoCTF.Tests.Integration.Persistence;
using Testcontainers.PostgreSql;
using NSubstitute;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeWriteBackOrderingTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Provision_success_inserts_receipt_ports_as_new_relational_rows(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .Build();
            await postgres.StartAsync(ct);
            await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options);
            await db.Database.EnsureCreatedAsync(ct);
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(db, ct);
            var runtime = await db.RuntimeInstances.SingleAsync(
                row => row.Id == fixture.RuntimeIds[0], ct);
            runtime.State = RuntimeState.Provisioning;
            runtime.StoppedAt = null;
            runtime.RunnerId = "runner";
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            var handler = new RuntimeProvisionWriteBackMessageHandler(
                db, new NoOpPostCommitMessagePublisher(),
                NullCompetitionEventRecorder.Instance, TimeProvider.System,
                Substitute.For<IRunnerCapacityGate>());
            await handler.Handle(new RuntimeProvisioned(
                runtime.Id,
                "runner",
                RuntimeProvider.Docker,
                RuntimeReceiptTestData.ContainerData(runtime.Id) with
                {
                    PortMappings = new Dictionary<int, int> { [8080] = 49152 }
                },
                [new RuntimeAccessEndpointMapping(0, "127.0.0.1:49152", null, null)],
                null,
                [new RuntimePublishedPortMapping(null, 8080, 49152)]), ct);

            db.ChangeTracker.Clear();
            var persisted = await db.RuntimeInstances.AsNoTracking()
                .SingleAsync(row => row.Id == runtime.Id, ct);
            var receipt = await db.Set<RuntimeReceipt>().AsNoTracking()
                .SingleAsync(row => row.RuntimeInstanceId == runtime.Id, ct);
            await Assert.That(persisted.State).IsEqualTo(RuntimeState.Running);
            await Assert.That(receipt.PortMappings.Select(port => port.HostPort))
                .Contains(49152);
        });
    }

    [Test, Arguments(RuntimeState.Stopping), Arguments(RuntimeState.Stopped), Timeout(300_000)]
    public async Task Late_provision_results_do_not_revert_a_cleanup_transition(RuntimeState state, CancellationToken ct)
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
            runtime.ProviderReceipt = RuntimeReceiptTestData.ContainerEntity();
            await db.SaveChangesAsync(ct);
            await db.Entry(runtime).ReloadAsync(ct);
            var receiptOperationId = runtime.ProviderReceipt!.OperationId;
            var runningAt = runtime.RunningAt;
            var before = await db.CompetitionEvents.CountAsync(ct);
            var handler = new RuntimeProvisionWriteBackMessageHandler(db, new NoOpPostCommitMessagePublisher(),
                NullCompetitionEventRecorder.Instance, TimeProvider.System, Substitute.For<IRunnerCapacityGate>());
            await handler.Handle(new RuntimeProvisionFailed(runtime.Id, RuntimeFailureCode.RunnerUnavailable, "runner"), ct);
            await handler.Handle(new RuntimeProvisioned(
                runtime.Id,
                "runner",
                RuntimeProvider.Docker,
                RuntimeReceiptTestData.ContainerData(runtime.Id),
                [],
                null), ct);
            await db.Entry(runtime).ReloadAsync(ct);
            await Assert.That(runtime.State).IsEqualTo(state);
            await Assert.That(runtime.FailureCode).IsNull();
            await Assert.That(runtime.RunningAt).IsEqualTo(runningAt);
            var receipt = await db.Set<RuntimeReceipt>().AsNoTracking()
                .SingleAsync(item => item.RuntimeInstanceId == runtime.Id, ct);
            await Assert.That(receipt).IsTypeOf<ContainerRuntimeReceipt>();
            await Assert.That(((ContainerRuntimeReceipt)receipt).ResourceId)
                .IsEqualTo("test-runtime");
            await Assert.That(receipt.OperationId).IsEqualTo(receiptOperationId);
            await Assert.That(await db.CompetitionEvents.CountAsync(ct)).IsEqualTo(before);
        });
    }
}
