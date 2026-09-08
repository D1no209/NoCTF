using DotNet.Testcontainers.Builders;
using JasperFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Hosting;
using NoCTF.Application.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner;
using NoCTF.Tests.Integration.Persistence;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Nats;
using Wolverine.Postgresql;
using Wolverine.EntityFrameworkCore;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[NotInParallel]
public sealed class RunnerProvisionDeliveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Durable_provision_command_executes_handler_and_publishes_result(bool fullRegistration, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            const string runnerId = "delivery-test-runner";
            var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = Substitute.For<IRuntimeNodeWorkReader>();
            work.ReadProvisionStatusAsync(Arg.Any<IRuntimeProvisionMessage>(), Arg.Any<CancellationToken>())
                .Returns(_ => { called.TrySetResult(); return Task.FromResult(RuntimeProvisionWorkStatus.AssignmentAbsent); });
            var capacity = Substitute.For<IRunnerCapacityGate>();
            var provider = new RuntimeProviderHandler(Substitute.For<IRuntimeProviderCatalog>(), [],
                Options.Create(new RunnerOptions { Id = runnerId, Pool = "default", Provider = RuntimeProvider.Docker }), capacity, work);
            var probe = new ResultProbe();
            var fixture = new CompetitionForceDeleteFixture();
            var subject = NatsSubjects.Runner(RunnerNodeQueueName.FromRunnerId(runnerId).Value);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runner:Id"] = runnerId, ["Runner:Pool"] = "default", ["Runner:Provider"] = "Docker",
                ["Runner:Capacity:MemoryBytes"] = "67108864", ["Runner:Capacity:NanoCpus"] = "100000000", ["Runner:Capacity:PidsLimit"] = "64",
                ["Runner:Heartbeat:IntervalSeconds"] = "5", ["Runner:Heartbeat:TtlSeconds"] = "15",
                ["RunnerScoring:CallbackBaseUrl"] = "http://127.0.0.1:8080", ["RunnerScoring:SigningKey"] = new string('x', 64),
                ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString()
            }).Build();
            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    if (fullRegistration)
                    {
                        services.AddNoCtfStandaloneRunnerPersistence(configuration);
                        services.AddNoCtfRunner(configuration, development: true);
                        services.Replace(ServiceDescriptor.Singleton(work));
                        services.AddSingleton(capacity);
                    }
                    else services.AddSingleton(provider);
                    services.AddSingleton(probe);
                })
                .UseWolverine(options =>
                {
                    options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
                    options.Discovery.DisableConventionalDiscovery();
                    options.Discovery.IncludeType(typeof(ContainerRuntimeMessageHandler));
                    if (fullRegistration)
                    {
                        options.Discovery.IncludeType(typeof(RuntimeProvisionWriteBackMessageHandler));
                        options.Discovery.IncludeType(typeof(RuntimeStopWriteBackMessageHandler));
                    }
                    options.Discovery.IncludeType(typeof(ProvisionResultHandler));
                    options.Discovery.IncludeType(typeof(RuntimeEventHandler));
                    options.PersistMessagesWithPostgresql(postgres.GetConnectionString(), "runner_delivery_test");
                    if (fullRegistration) options.UseEntityFrameworkCoreTransactions();
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                    options.UseNats($"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}").AutoProvision()
                        .UseJetStream(_ => { }).DefineWorkQueueStream(NatsSubjects.RunnerStream,
                            stream => stream.WithSubject("noctf.runner.>"), "noctf.runner.>")
                        .DefineWorkQueueStream(NatsSubjects.EventsStream,
                            stream => stream.WithSubject(NatsSubjects.RealtimeEvents), NatsSubjects.RealtimeEvents);
                    options.PublishMessage<CompetitionEventCommitted>()
                        .ToNatsSubject(NatsSubjects.RealtimeEvents).UseJetStream(NatsSubjects.EventsStream).UseDurableOutbox();
                    options.ListenToNatsSubject(NatsSubjects.RealtimeEvents)
                        .UseJetStream(NatsSubjects.EventsStream, "delivery-event-test").UseDurableInbox();
                    options.ListenToNatsSubject(subject).UseJetStream(NatsSubjects.RunnerStream, "delivery-test").UseDurableInbox();
                    options.Policies.Add(new NoCTF.Hosting.Messaging.DurableRunnerCommandPolicy());
                }).Build();
            if (fullRegistration)
            {
                await using var scope = host.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>().Database.EnsureCreatedAsync(ct);
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await fixture.SeedAsync(db, ct);
                var runtime = await db.RuntimeInstances.SingleAsync(x => x.Id == fixture.RuntimeIds[0], ct);
                runtime.State = RuntimeState.Stopping;
                runtime.RunnerId = runnerId;
                await db.SaveChangesAsync(ct);
            }
            await host.StartAsync(ct);
            try
            {
                var id = Guid.CreateVersion7();
                var command = new ProvisionContainerRuntime(id, runnerId, new ContainerRequest(id, RuntimeProvider.Docker,
                    "fixture:unused", [], new Dictionary<string, string>(), new Dictionary<string, string>(),
                    new Dictionary<int, int>(), new(67108864, 100000000, 64), new(true, false, true, ["ALL"], []), null));
                await host.Services.GetRequiredService<IMessageBus>().EndpointFor(new Uri("nats://subject/" + subject)).SendAsync(command);
                await called.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                var result = await probe.Result.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                await Assert.That(result.RuntimeInstanceId).IsEqualTo(id);
                await Assert.That(result.FailureCode).IsEqualTo(RuntimeFailureCode.RunnerUnavailable);
                if (fullRegistration)
                {
                    await host.Services.GetRequiredService<IMessageBus>().EndpointFor(new Uri("nats://subject/" + subject))
                        .SendAsync(new StopContainerRuntime(fixture.RuntimeIds[0], runnerId));
                    var observed = RuntimeState.Stopping;
                    for (var attempt = 0; attempt < 40 && observed != RuntimeState.Stopped; attempt++)
                    {
                        await using var scope = host.Services.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                        observed = (await db.RuntimeInstances.AsNoTracking().SingleAsync(x => x.Id == fixture.RuntimeIds[0], ct)).State;
                        await Task.Delay(250, ct);
                    }
                    await Assert.That(observed).IsEqualTo(RuntimeState.Stopped);
                    await Assert.That((await probe.Event.Task.WaitAsync(TimeSpan.FromSeconds(20), ct)).CompetitionId).IsEqualTo(fixture.Id);
                }
            }
            finally { await host.StopAsync(ct); }
        });
    }

    public sealed class ResultProbe
    {
        public TaskCompletionSource<RuntimeProvisionFailed> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CompetitionEventCommitted> Event { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class ProvisionResultHandler(ResultProbe probe)
    {
        public void Handle(RuntimeProvisionFailed result) => probe.Result.TrySetResult(result);
    }

    public sealed class RuntimeEventHandler(ResultProbe probe)
    {
        public void Handle(CompetitionEventCommitted message) => probe.Event.TrySetResult(message);
    }
}
