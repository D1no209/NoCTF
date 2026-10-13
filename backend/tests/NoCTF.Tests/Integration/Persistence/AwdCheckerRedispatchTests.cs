using System.Threading.Channels;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Runtime;
using NoCTF.Hosting;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Hosting.Messaging;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Checker_recovers_over_JetStream_after_a_claim_concurrency_conflict(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct, includeChecker: true);
            var clock = new MutableTimeProvider(fixture.Now);
            var outbox = new RecordingOutbox();
            await using (var setup = new NoCtfDbContext(options))
            {
                var template = await setup.Challenges.SingleAsync(ct);
                ((AwdChallengeDefinition)template.Definition!).Checker!.TargetServiceName = "main";
                var runtime = await setup.RuntimeInstances.SingleAsync(ct);
                runtime.ProviderReceipt = RuntimeReceiptTestData.ContainerEntity(runtime.Id);
                await setup.SaveChangesAsync(ct);
                var rounds = new AwdRoundCoordinator(setup, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
                await rounds.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
                await rounds.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Last(), ct);
            }
            async Task<RunAwdChecker> Dispatch()
            {
                await using var db = new NoCtfDbContext(options);
                await new AwdCheckerCoordinator(db, outbox, clock, NullCompetitionEventRecorder.Instance)
                    .DispatchAsync(new(clock.UtcNow), ct);
                return outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            }
            var first = await Dispatch();
            var discoveryCalls = 0;
            var provider = Substitute.For<IContainerRuntime>();
            provider.GetStatusAsync(Arg.Any<ContainerDeploymentReceipt>(), Arg.Any<CancellationToken>())
                .Returns(async _ =>
                {
                    // Deterministically invalidate the reader's loaded fact before it claims execution.
                    if (Interlocked.Increment(ref discoveryCalls) == 1)
                    {
                        await using var concurrent = new NoCtfDbContext(options);
                        var fact = await concurrent.GameplayFacts.SingleAsync(item => item.Id == first.GameplayFactId, ct);
                        fact.ConcurrencyStamp = Guid.NewGuid();
                        await concurrent.SaveChangesAsync(ct);
                    }
                    return (ContainerRuntimeStatus?)new ContainerRuntimeStatus("awd-test", RuntimeStatus.Running,
                        [new("main", "test-runtime", RuntimeStatus.Running, new Dictionary<int, int>(), "runtime.internal")]);
                });
            var providers = Substitute.For<IRuntimeProviderCatalog>();
            providers.Runtime(RuntimeProvider.Docker).Returns(provider);
            var tokens = new RunnerScoringTokenIssuer(Options.Create(new RunnerScoringOptions
                { SigningKey = new string('x', 32), Issuer = "awd-test", Audience = "awd-test" }));
            var reader = new AwdCheckerWorkReader(new TestContextFactory(options), new(), tokens, providers,
                Options.Create(new RunnerScoringOptions { CallbackBaseUrl = new Uri("http://localhost:5080") }), clock);
            var probe = new CheckerRedispatchProbe(reader);
            var subject = $"noctf.v2.runner.{RunnerNodeQueueName.FromRunnerId(first.RunnerId).Value}";
            using var host = Host.CreateDefaultBuilder().ConfigureServices(services => services.AddSingleton(probe))
                .UseWolverine(wolverine =>
                {
                    wolverine.Discovery.DisableConventionalDiscovery();
                    wolverine.Discovery.IncludeType(typeof(CheckerRedispatchProbeHandler));
                    wolverine.Policies.Add(new DurableRunnerCommandPolicy());
                    wolverine.UseNats($"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}")
                        .AutoProvision().UseJetStream(_ => { })
                        .DefineWorkQueueStream(NatsSubjects.RunnerStream, configuration => configuration.WithSubjects(subject), subject);
                    wolverine.ListenToNatsSubject(subject).UseJetStream(NatsSubjects.RunnerStream, "awd-redispatch-test");
                }).Build();
            await host.StartAsync(ct);
            try
            {
                await using var publisherDb = new NoCtfDbContext(options);
                var publisher = new WolverinePostCommitMessagePublisher(host.Services.GetRequiredService<IMessageBus>(), publisherDb);
                async Task Send(RunAwdChecker command)
                {
                    await publisher.PublishToRunnerNodeAsync(command);
                    await publisher.FlushCommittedMessagesAsync();
                }
                await Send(first);
                await Assert.That(await probe.Claims.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(20), ct)).IsFalse();
                await using (var verify = new NoCtfDbContext(options))
                {
                    var fact = await verify.GameplayFacts.Include(item => item.Awd).SingleAsync(item => item.Id == first.GameplayFactId, ct);
                    await Assert.That(fact.Awd!.Execution).IsEqualTo(AwdCheckExecution.Pending);
                    await Assert.That(fact.Awd.AttemptStartedAt).IsNull();
                }
                var recovery = await Dispatch();
                await Assert.That(recovery.AttemptId).IsEqualTo(first.AttemptId);
                await Send(recovery);
                await Assert.That(await probe.Claims.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(8), ct)).IsTrue();
                // The same execution fence still rejects duplicate deliveries after successful recovery.
                await Assert.That(await reader.ReadAsync(recovery, ct)).IsNull();
            }
            finally { await host.StopAsync(ct); }
        });
    }

    public sealed class CheckerRedispatchProbe(IAwdCheckerWorkReader reader)
    {
        public Channel<bool> Claims { get; } = Channel.CreateUnbounded<bool>();
        public async Task ReceiveAsync(RunAwdChecker message, CancellationToken ct) =>
            await Claims.Writer.WriteAsync(await reader.ReadAsync(message, ct) is not null, ct);
    }

    public sealed class CheckerRedispatchProbeHandler(CheckerRedispatchProbe probe)
    {
        public Task Handle(RunAwdChecker message, CancellationToken ct) => probe.ReceiveAsync(message, ct);
    }
}
