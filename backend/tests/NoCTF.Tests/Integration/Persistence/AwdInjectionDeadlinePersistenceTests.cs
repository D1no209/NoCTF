using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Messages;
using NoCTF.Runner.Composition;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test, Timeout(300_000), Arguments(false), Arguments(true)]
    public Task Runner_command_delivery_uses_effective_end_after_scheduled_EndAt(bool totalRounds, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        var fixture = await SeedAsync(options, ct);
        await using var db = new NoCtfDbContext(options);
        var competition = await db.Competitions.SingleAsync(ct);
        competition.EndAt = fixture.Now.AddSeconds(totalRounds ? 180 : 360);
        var configuration = (AwdCompetitionModeConfiguration)competition.ModeConfiguration!;
        configuration.RoundDurationSeconds = 180;
        configuration.AwdOptions = configuration.AwdOptions with { TotalRounds = totalRounds ? 3 : null, RoundGapSeconds = totalRounds ? 0 : 30 };
        var template = await db.Challenges.SingleAsync(ct);
        var store = ((AwdChallengeDefinition)template.Definition!).FlagStores.Single();
        store.DeliveryMode = AwdFlagDeliveryMode.RunnerCommand;
        store.InjectionCommand = "printf '%s' \"$FLAG\" > /tmp/noctf-issued-flag";
        store.TargetServiceName = "web";
        await db.SaveChangesAsync(ct);
        var clock = new MutableTimeProvider(fixture.Now);
        var outbox = new RecordingOutbox();
        var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
        await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
        outbox.Published.Clear();
        clock.UtcNow = fixture.Now.AddSeconds(375);
        await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
        await coordinator.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Single(), ct);
        var injection = outbox.RunnerNodeMessages.OfType<InjectAwdFlag>().Single();
        var reader = new AwdFlagInjectionWorkReader(new TestContextFactory(options), clock);
        var runtime = new DeliveryTestRuntime(postgres);
        var providers = Substitute.For<IRuntimeProviderCatalog>();
        providers.Runtime(RuntimeProvider.Docker).Returns(runtime);
        var handler = new AwdFlagInjectionHandler(reader, new AwdFlagInjectionExecutor(providers), outbox,
            Options.Create(new RunnerOptions { Id = injection.RunnerId, Pool = "test" }), clock);
        await Assert.That(await handler.Handle(injection, ct)).IsEqualTo(MessageExecutionOutcome.Applied);
        await Assert.That(runtime.Timeout.TotalSeconds).IsEqualTo(totalRounds ? 30d : 15d);
        var written = await postgres.ExecAsync(["cat", "/tmp/noctf-issued-flag"], ct);
        await Assert.That(written.Stdout).IsEqualTo(await db.ChallengeFlags.Select(flag => flag.Flag).SingleAsync(ct));
        await Assert.That(await reader.ReadAsync(injection, ct)).IsNull();
        var issue = await db.AwdFlagIssues.AsNoTracking().SingleAsync(ct);
        await Assert.That(issue.DeliveryState).IsEqualTo(AwdFlagDeliveryState.Succeeded);
        await Assert.That(issue.Obligation).IsEqualTo(AwdFlagObligation.PlayerRequired);
        await Assert.That(competition.EndAt).IsEqualTo(fixture.Now.AddSeconds(totalRounds ? 180 : 360));
        clock.UtcNow = injection.ValidUntil;
        await Assert.That(await reader.ReadAsync(injection, ct)).IsNull();
    });

    private sealed class DeliveryTestRuntime(PostgreSqlContainer container) : IContainerRuntime
    {
        public TimeSpan Timeout { get; private set; }
        public Task<ContainerDeploymentReceipt> UpAsync(ContainerRuntimeRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DownAsync(ContainerDeploymentReceipt receipt, CancellationToken ct) => throw new NotSupportedException();
        public Task<ContainerRuntimeStatus?> GetStatusAsync(ContainerDeploymentReceipt receipt, CancellationToken ct) => throw new NotSupportedException();
        public async Task<ContainerExecResult> ExecAsync(ContainerDeploymentReceipt receipt, string serviceName,
            IReadOnlyList<string> command, TimeSpan timeout, CancellationToken ct)
        {
            Timeout = timeout;
            await Assert.That(serviceName).IsEqualTo("web");
            var result = await container.ExecAsync(command.ToArray(), ct);
            return new((int)(result.ExitCode ?? -1), false);
        }
    }
}
