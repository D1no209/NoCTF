using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentStorage;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Net;
using NATS.Client.JetStream.Models;
using NoCTF.Application.Challenges.Actions;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Competitions.Awd.Fixes;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Hosting;
using NoCTF.Hosting.Messaging;
using NoCTF.Infrastructure.Challenges.Actions;
using NoCTF.Infrastructure.Challenges.Capabilities;
using NoCTF.Infrastructure.Competitions.Awd.Fixes;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.GameplayFacts.PatchUploads;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Docker.Containers;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class PersistentFixPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Real_mutation_wait_that_crosses_a_check_deadline_records_maintenance_without_probe_results(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        await using var session = fixture.OpenSession();
        var accepted = await session.UploadAsync("sleep 3; printf repaired > /repair-state", ct);
        await Assert.That(accepted.Succeeded).IsTrue();
        var fix = await session.Db.GameplayFacts.AsSplitQuery().SingleAsync(ct);
        var work = await session.Fixes.ClaimAsync(new(fix.Id, fixture.RuntimeId, fix.FixExecution!.Deadline, Fixture.RunnerId), DateTimeOffset.UtcNow, ct);
        await Assert.That(work).IsNotNull();
        var start = DateTimeOffset.UtcNow; start = start.AddTicks(-(start.Ticks % TimeSpan.TicksPerMicrosecond));
        var deadline = start.AddSeconds(2);
        var round = new AwdRound { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId, Number = 1,
            StartsAt = start, EndsAt = start.AddMinutes(1), ActiveEndTicks = TimeSpan.FromMinutes(1).Ticks };
        var rules = new AwdRoundRules { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId, RoundId = round.Id,
            CompetitionChallengeId = fixture.ChallengeId, EffectiveAt = start, CheckerIntervalSeconds = 60 };
        var check = new CheckObservationGameplayFact { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.ChallengeId, TeamId = fix.TeamId, OccurredAt = start, UpdatedAt = start,
            State = GameplayFactState.Queued, ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = round.Id,
            Awd = new() { RulesId = rules.Id, RuntimeId = fixture.RuntimeId, AttemptId = Guid.NewGuid(), ScheduledAt = start,
                Deadline = deadline, ReceiptDeadline = deadline.AddSeconds(30), ReceivedRoundId = round.Id, ReceivedRoundNumber = 1,
                SampleActiveTicks = 0, SampleDeadlineActiveTicks = TimeSpan.FromSeconds(2).Ticks, ManifestComplete = true } };
        session.Db.AwdRounds.Add(round); session.Db.AwdRoundRules.Add(rules); session.Db.GameplayFacts.Add(check);
        await session.Db.SaveChangesAsync(ct);
        var message = new RunAwdChecker(fixture.RuntimeId, fixture.ChallengeId, check.Id, Fixture.RunnerId,
            deadline, check.Awd.AttemptId!.Value, check.Awd.ReceiptDeadline!.Value);
        using var mutations = new RunnerResourceMutationCoordinator();
        var holding = await mutations.EnterRuntimeAsync(fixture.RuntimeId, ct);
        var execution = fixture.ApplyAsync(session.Db, work!, ct);
        var waitStartedAt = DateTimeOffset.UtcNow;
        var waiting = mutations.EnterRuntimeAsync(fixture.RuntimeId, ct);
        await Assert.That(await session.Fixes.CompleteAsync(await execution, DateTimeOffset.UtcNow, ct)).IsTrue();
        holding.Dispose();
        using var acquired = await waiting;
        await Assert.That(acquired.Waited).IsTrue();
        await AwdMaintenanceEvidence.RecordCheckerWaitAsync(session.Db, message, deadline, DateTimeOffset.UtcNow, ct);
        session.Db.ChangeTracker.Clear();
        await Assert.That((await session.Db.GameplayFacts.Include(value => value.Awd).AsSplitQuery().SingleAsync(value => value.Id == check.Id, ct)).Awd!.BlockingFixFactId).IsNull();
        await AwdMaintenanceEvidence.RecordCheckerWaitAsync(session.Db, message, waitStartedAt, DateTimeOffset.UtcNow, ct);
        session.Db.ChangeTracker.Clear();
        var persisted = await session.Db.GameplayFacts.Include(value => value.Awd).AsSplitQuery().SingleAsync(value => value.Id == check.Id, ct);
        await Assert.That(persisted.Awd!.BlockingFixFactId).IsEqualTo(fix.Id);
        await new AwdCheckerCoordinator(session.Db, session.Publisher, TimeProvider.System,
            new CompetitionEventStore(session.Db, session.Publisher)).DispatchAsync(new(DateTimeOffset.UtcNow), ct);
        session.Db.ChangeTracker.Clear();
        var settled = await session.Db.GameplayFacts.Include(value => value.Awd).AsSplitQuery().SingleAsync(value => value.Id == check.Id, ct);
        await Assert.That(settled.Result).IsEqualTo(GameplayFactResult.ServiceDown);
        await Assert.That(settled.Awd!.Reason).IsEqualTo(AwdCheckReason.PlayerMaintenance);
        await Assert.That(settled.Awd.Settlement).IsEqualTo(AwdSettlementState.Final);
        await Assert.That(settled.Awd.Connection).IsEqualTo(AwdProbeOutcome.Unknown);
        await Assert.That(settled.Awd.Functionality).IsEqualTo(AwdProbeOutcome.Unknown);
        await Assert.That(settled.CapabilityOutcomes.OfType<CheckOutcome>().Single().Probes.All(probe => probe.Result == CheckProbeResult.NotExecuted)).IsTrue();
    }

    [Test, Timeout(300_000)]
    public async Task Successive_persistent_fixes_keep_the_same_runtime_and_current_evidence(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        await using var deliveries = await fixture.Connection.SubscribeCoreAsync<byte[]>("noctf.>", cancellationToken: ct);
        await fixture.Connection.PingAsync(ct);
        await using var session = fixture.OpenSession();
        var first = await session.UploadAsync("printf first > /repair-state", ct);
        await Assert.That(first.Succeeded).IsTrue();
        var firstId = first.Value!.GameplayFactId;
        var deadline = (await session.Db.GameplayFacts.AsSplitQuery().SingleAsync(fact => fact.Id == firstId, ct)).FixExecution!.Deadline;
        var command = new RunPersistentFix(firstId, fixture.RuntimeId, deadline, Fixture.RunnerId);
        var claimed = await session.Fixes.ClaimAsync(command, DateTimeOffset.UtcNow, ct);
        await Assert.That(claimed).IsNotNull();
        await Assert.That(await session.Fixes.ClaimAsync(command, DateTimeOffset.UtcNow, ct)).IsNull();
        var busy = await session.Actions.ReadAsync(fixture.CompetitionId, fixture.ChallengeId, fixture.UserId, DateTimeOffset.UtcNow, ct);
        await Assert.That(busy.Single(action => action.Kind == ChallengeActionKind.SubmitFix).Availability).IsEqualTo(ChallengeActionAvailability.TargetBusy);
        var concurrent = await session.UploadAsync("printf unwanted > /repair-state", ct);
        await Assert.That(concurrent.Succeeded).IsFalse();
        var applied = await fixture.ApplyAsync(session.Db, claimed!, ct);
        await Assert.That(await session.Fixes.CompleteAsync(applied, DateTimeOffset.UtcNow, ct)).IsTrue();
        var second = await session.UploadAsync("printf second >> /repair-state", ct);
        await Assert.That(second.Succeeded).IsTrue();
        var secondFact = await session.Db.GameplayFacts.AsSplitQuery().SingleAsync(fact => fact.Id == second.Value!.GameplayFactId, ct);
        var secondWork = await session.Fixes.ClaimAsync(new(secondFact.Id, fixture.RuntimeId, secondFact.FixExecution!.Deadline, Fixture.RunnerId), DateTimeOffset.UtcNow, ct);
        await Assert.That(secondWork).IsNotNull();
        await Assert.That(await session.Fixes.CompleteAsync(await fixture.ApplyAsync(session.Db, secondWork!, ct), DateTimeOffset.UtcNow, ct)).IsTrue();
        session.Db.ChangeTracker.Clear();
        await Assert.That(await session.Db.PatchUploads.CountAsync(upload => upload.RuntimeInstanceId == fixture.RuntimeId, ct)).IsEqualTo(2);
        await Assert.That(await session.Db.GameplayFacts.CountAsync(fact => fact.Kind == GameplayFactKind.FixAttempt && fact.Result == GameplayFactResult.Applied, ct)).IsEqualTo(2);
        await Assert.That(await session.Db.RuntimeInstances.CountAsync(runtime => runtime.Id == fixture.RuntimeId && runtime.State == RuntimeState.Running, ct)).IsEqualTo(1);
        var contents = await fixture.Target.ExecAsync(["cat", "/repair-state"], ct);
        await Assert.That(contents.Stdout).IsEqualTo("firstsecond");
        var actions = await session.Actions.ReadAsync(fixture.CompetitionId, fixture.ChallengeId, fixture.UserId, DateTimeOffset.UtcNow, ct);
        await Assert.That(actions.Single(action => action.Kind == ChallengeActionKind.SubmitFix).Enabled).IsTrue();
        var stream = await fixture.Connection.CreateJetStreamContext().GetStreamAsync(NatsSubjects.RunnerStream, cancellationToken: ct);
        for (var attempt = 0; stream.Info.State.Messages == 0 && attempt < 50; attempt++)
        {
            await Task.Delay(100, ct);
            stream = await fixture.Connection.CreateJetStreamContext().GetStreamAsync(NatsSubjects.RunnerStream, cancellationToken: ct);
        }
        await fixture.Connection.PingAsync(ct);
        var subjects = new List<string>();
        while (deliveries.Msgs.TryRead(out var delivered)) subjects.Add(delivered.Subject);
        await Assert.That(subjects).Contains(NatsSubjects.Runner(NoCTF.Application.Runtime.Instances.RunnerNodeQueueName.FromRunnerId(Fixture.RunnerId).Value));
        await Assert.That(stream.Info.State.Messages).IsGreaterThanOrEqualTo(1);
    }

    [Test, Timeout(300_000)]
    public async Task Persistent_timeout_releases_the_target_only_after_observed_completion(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        await using var session = fixture.OpenSession();
        var accepted = await session.UploadAsync("printf started > /repair-state; sleep 2; printf ended >> /repair-state", ct);
        await Assert.That(accepted.Succeeded).IsTrue();
        var fact = await session.Db.GameplayFacts.AsSplitQuery().SingleAsync(value => value.Id == accepted.Value!.GameplayFactId, ct);
        var work = await session.Fixes.ClaimAsync(new(fact.Id, fixture.RuntimeId, fact.FixExecution!.Deadline, Fixture.RunnerId), DateTimeOffset.UtcNow, ct);
        var outcome = await fixture.ApplyAsync(session.Db, work!, ct, TimeSpan.FromMilliseconds(150));
        await Assert.That(outcome.ExecutionUncertain).IsTrue();
        await Assert.That(await session.Fixes.CompleteAsync(outcome, DateTimeOffset.UtcNow, ct)).IsTrue();
        var state = await fixture.Target.ExecAsync(["/bin/sh", "-c", "test -f /repair-state"], ct);
        await Assert.That(state.ExitCode).IsEqualTo(0L);
        var actions = await session.Actions.ReadAsync(fixture.CompetitionId, fixture.ChallengeId, fixture.UserId, DateTimeOffset.UtcNow, ct);
        await Assert.That(actions.Single(action => action.Kind == ChallengeActionKind.SubmitFix).Availability).IsEqualTo(ChallengeActionAvailability.RecoveryRequired);
        await Assert.That((await session.UploadAsync("printf overwrite > /repair-state", ct)).Succeeded).IsFalse();
        var observationWork = await session.Fixes.ReadObservationAsync(new(fact.Id, fixture.RuntimeId, Fixture.RunnerId, 0), ct);
        await Assert.That(observationWork).IsNotNull();
        var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
            ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
        using var lifecycle = new DockerContainerLifecycle(new(Endpoint: endpoint, NetworkName: "none"));
        var observation = await lifecycle.ObserveExecAsync(observationWork!.Receipt.ServiceReceipt(observationWork.ServiceName), observationWork.ExecutionIdentity, ct);
        for (var attempt = 0; observation.State != ContainerExecState.Ended && attempt < 50; attempt++)
        {
            await Task.Delay(100, ct);
            observation = await lifecycle.ObserveExecAsync(observationWork.Receipt.ServiceReceipt(observationWork.ServiceName), observationWork.ExecutionIdentity, ct);
        }
        await Assert.That(observation.State).IsEqualTo(ContainerExecState.Ended);
        await session.Fixes.RecordObservationAsync(observationWork, observation, DateTimeOffset.UtcNow, ct);
        var recovered = await session.Actions.ReadAsync(fixture.CompetitionId, fixture.ChallengeId, fixture.UserId, DateTimeOffset.UtcNow, ct);
        await Assert.That(recovered.Single(action => action.Kind == ChallengeActionKind.SubmitFix).Enabled).IsTrue();
        var contents = await fixture.Target.ExecAsync(["cat", "/repair-state"], ct);
        await Assert.That(contents.Stdout).IsEqualTo("startedended");
        await Assert.That(await session.Db.PatchUploads.CountAsync(ct)).IsEqualTo(1);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string RunnerId = "persistent-fix-test";
        private const string Image = "docker.m.daocloud.io/library/alpine@sha256:2e0b81d60597f449baa56dc25d355367869d2dca4cd57e866f1e2b52af3eb55f";
        private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_persistent_fix_{Guid.NewGuid():N}").Build();
        private readonly IContainer nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.14.7-alpine@sha256:4063edae0717ba5f7501bfde75f97fd9b57f5b93597b92c70b6a6fbbf6a74e06")
            .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
        public IContainer Target { get; } = new ContainerBuilder(Image).WithCommand("sleep", "3600").Build();
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public Guid ChallengeId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid RuntimeId { get; } = Guid.NewGuid();
        private Guid TeamId { get; } = Guid.NewGuid();
        private readonly string directory = Path.Combine(AppContext.BaseDirectory, "persistent-fix-data", Guid.NewGuid().ToString("N"));
        private IHost? messaging;
        public NatsConnection Connection { get; private set; } = null!;
        public DbContextOptions<NoCtfDbContext> Options { get; private set; } = null!;
        public IStore Objects { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var fixture = new Fixture();
            try
            {
                await Task.WhenAll(fixture.postgres.StartAsync(ct), fixture.nats.StartAsync(ct), fixture.Target.StartAsync(ct));
                fixture.Options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(fixture.postgres.GetConnectionString()).Options;
                Directory.CreateDirectory(fixture.directory);
                fixture.Objects = StorageFactory.Disk(Path.Combine(fixture.directory, "objects"));
                var url = $"nats://{fixture.nats.Hostname}:{fixture.nats.GetMappedPublicPort(4222)}";
                fixture.Connection = new(new NatsOpts { Url = url });
                var subject = NatsSubjects.Subject(WorkerQueue.Background);
                var runnerSubject = NatsSubjects.Runner(NoCTF.Application.Runtime.Instances.RunnerNodeQueueName.FromRunnerId(RunnerId).Value);
                fixture.messaging = Host.CreateDefaultBuilder().ConfigureServices(services =>
                    services.AddDbContextFactory<NoCtfDbContext>(builder => builder.UseNpgsql(fixture.postgres.GetConnectionString())))
                    .UseWolverine(options =>
                {
                    options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
                    options.Discovery.DisableConventionalDiscovery(); options.Policies.Add(new DurableRunnerCommandPolicy());
                    options.UseNats(url).AutoProvision().UseJetStream(_ => { })
                        .DefineWorkQueueStream(NatsSubjects.BackgroundStream, stream => stream.WithSubjects(subject, NatsSubjects.ScheduledSubject(WorkerQueue.Background)).EnableScheduledDelivery(), subject)
                        .DefineWorkQueueStream(NatsSubjects.RunnerStream, stream => stream.WithSubject(runnerSubject), runnerSubject);
                    options.PublishMessage<GameplayFactStateChanged>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                    options.PublishMessage<NoCTF.Application.Competitions.Events.CompetitionEventCommitted>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                    options.PublishMessage<CleanupFile>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                }).Build();
                await fixture.messaging.StartAsync(ct);
                await fixture.Connection.CreateJetStreamContext().CreateOrUpdateConsumerAsync(NatsSubjects.RunnerStream,
                    new ConsumerConfig("persistent-fix-runner") { AckPolicy = ConsumerConfigAckPolicy.Explicit, FilterSubject = runnerSubject }, ct);
                await fixture.Connection.CreateJetStreamContext().CreateOrUpdateConsumerAsync(NatsSubjects.BackgroundStream,
                    new ConsumerConfig("persistent-fix-events") { AckPolicy = ConsumerConfigAckPolicy.Explicit, FilterSubject = subject }, ct);
                await fixture.SeedAsync(ct);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task SeedAsync(CancellationToken ct)
        {
            await using var db = new NoCtfDbContext(Options); await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow; var templateId = Guid.NewGuid();
            db.Users.Add(new() { Id = UserId, UserName = "maintainer", NormalizedUserName = "MAINTAINER", Email = "maintainer@example.test",
                NormalizedEmail = "MAINTAINER@EXAMPLE.TEST", AccountStatus = UserAccountStatus.Active, Kind = UserKind.Human });
            db.Competitions.Add(new AwdCompetition { Id = CompetitionId, OwnerId = UserId, Title = "Persistent Fix", Status = CompetitionStatus.Running,
                StartAt = now.AddMinutes(-1), EndAt = now.AddHours(1), ModeConfiguration = new AwdCompetitionModeConfiguration() });
            db.Challenges.Add(new() { Id = templateId, OwnerId = UserId, Title = "Maintain service", Definition = new()
            {
                Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "web", Image = Image }] },
                Capabilities = [new BreakCapabilityDefinition(), new FixCapabilityDefinition { TargetServiceName = "web", Entrypoint = "fix.sh", TimeoutSeconds = 5 },
                    new CheckCapabilityDefinition { TargetServiceName = "web", Image = Image, Requirements = [new() { Kind = CheckItemKind.Functionality }] }]
            } });
            db.CompetitionChallenges.Add(new AwdCompetitionChallenge { Id = ChallengeId, CompetitionId = CompetitionId, ChallengeId = templateId,
                IsPublished = true, Rules = new AwdCompetitionChallengeRules { AllowFixSubmissions = true } });
            db.Teams.Add(new() { Id = TeamId, CompetitionId = CompetitionId, CaptainId = UserId, MemberIds = [UserId], Name = "maintenance",
                NormalizedName = "MAINTENANCE", InvitationToken = new string('a', 32), RegistrationStatus = TeamRegistrationStatus.Approved });
            db.RuntimeInstances.Add(new PlayerRuntimeInstance
            {
                Id = RuntimeId, CompetitionId = CompetitionId, CompetitionChallengeId = ChallengeId, TeamId = TeamId,
                RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, RunnerId = RunnerId, State = RuntimeState.Running,
                CreatedAt = now, RunningAt = now,
                ProviderReceipt = new ContainerRuntimeReceipt { RuntimeInstanceId = RuntimeId, OperationId = RuntimeId, Provider = RuntimeProvider.Docker,
                    ProjectName = $"noctf-rt-{RuntimeId:N}", Namespace = string.Empty, PublicHost = "localhost", CreatedAt = now,
                    Services = [new() { Id = Guid.NewGuid(), Name = "web", ResourceId = Target.Id, Status = RuntimeStatus.Running, InternalHost = Target.IpAddress }] }
            });
            await db.SaveChangesAsync(ct);
        }

        public Session OpenSession() => new(this, messaging!.Services);

        public async Task<FixOutcome> ApplyAsync(NoCtfDbContext db, PersistentFixWork work, CancellationToken ct, TimeSpan? timeout = null)
        {
            var file = await db.PatchUploads.AsNoTracking().Where(upload => upload.Id == work.Archive.UploadId).Select(upload => upload.File).SingleAsync(ct);
            await using var source = await Objects.OpenRead(file.ObjectKey, ct);
            var operation = Path.Combine(directory, work.GameplayFactId.ToString("N")); Directory.CreateDirectory(operation);
            var tar = Path.Combine(operation, "prepared.tar");
            await new FixArchivePreparer(Microsoft.Extensions.Options.Options.Create(new FixVerificationOptions()))
                .PrepareTarAsync(source, file.FileName, work.Definition.Entrypoint, Path.Combine(operation, "work"), tar, ct);
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            using var lifecycle = new DockerContainerLifecycle(new(Endpoint: endpoint, NetworkName: "none"));
            var target = work.Receipt.ServiceReceipt("web");
            if (!await new FixExecutionProgressStore(messaging!.Services.GetRequiredService<IDbContextFactory<NoCtfDbContext>>())
                    .RecordMutationStartedAsync(work.GameplayFactId, work.RuntimeInstanceId, FixTargetKind.Persistent, DateTimeOffset.UtcNow, ct))
                throw new InvalidOperationException("The accepted Fix no longer permits target modification.");
            await using (var archive = File.OpenRead(tar)) await lifecycle.CopyArchiveAsync(target, archive, ct);
            var execution = await lifecycle.ExecRetainingTargetAsync(target, FixCommand.Create(work.Definition), timeout ?? TimeSpan.FromSeconds(5), ct);
            return new() { GameplayFactId = work.GameplayFactId, RuntimeInstanceId = work.RuntimeInstanceId, ObservedAt = DateTimeOffset.UtcNow,
                ExecutionState = execution.PlatformFailure ? CapabilityExecutionState.PlatformFailed : CapabilityExecutionState.Completed,
                ExecutionFailure = execution.PlatformFailure ? CapabilityExecutionFailure.ExecutionInterrupted : null,
                Result = execution.PlatformFailure ? null : execution.TimedOut ? FixResult.TimedOut : execution.ExitCode == 0 ? FixResult.Applied : FixResult.Failed,
                ExitCode = execution.ExitCode, ExecutionUncertain = execution.ExecutionUncertain, ExecutionIdentity = execution.ExecutionIdentity };
        }

        public async ValueTask DisposeAsync()
        {
            if (messaging is not null) { await messaging.StopAsync(); messaging.Dispose(); }
            if (Connection is not null) await Connection.DisposeAsync();
            await Target.DisposeAsync(); await nats.DisposeAsync(); await postgres.DisposeAsync();
            Objects?.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        public sealed class Session : IAsyncDisposable
        {
            private readonly Fixture fixture;
            public NoCtfDbContext Db { get; }
            public AwdPersistentFixStore Fixes { get; }
            public AvailableChallengeActionsReader Actions { get; }
            public IPostCommitMessagePublisher Publisher { get; }
            private readonly CreatePatchUpload upload;
            public Session(Fixture fixture, IServiceProvider services)
            {
                this.fixture = fixture; Db = new(fixture.Options);
                var publisher = new WolverinePostCommitMessagePublisher(services.GetRequiredService<IMessageBus>(), Db,
                    services.GetRequiredService<ILogger<WolverinePostCommitMessagePublisher>>());
                Publisher = publisher;
                var events = new CompetitionEventStore(Db, publisher);
                Fixes = new(Db, publisher, events);
                Actions = new(Db, new ChallengeRuntimeTemplateCatalog(), new GameModeGameplayFactAdmissionPolicy());
                var registry = new ManagedFileUploadRegistry(Db, publisher, services.GetRequiredService<ILogger<ManagedFileUploadRegistry>>());
                upload = new(new PatchUploadStore(Db, publisher, new FileReferenceLock(), services.GetRequiredService<ILogger<PatchUploadStore>>(), events),
                    new ManagedFileUploads(registry, fixture.Objects));
            }

            public async Task<NoCTF.Application.Common.OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>> UploadAsync(string command, CancellationToken ct)
            {
                using var content = new MemoryStream();
                using (var gzip = new GZipStream(content, CompressionLevel.Fastest, leaveOpen: true))
                using (var tar = new TarWriter(gzip, leaveOpen: true))
                {
                    var bytes = Encoding.UTF8.GetBytes($"#!/bin/sh\nset -eu\n{command}\n");
                    using var body = new MemoryStream(bytes);
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = body });
                }
                content.Position = 0;
                return await upload.ExecuteAsync(fixture.CompetitionId, fixture.ChallengeId, fixture.RuntimeId, fixture.UserId,
                    "fix.tar.gz", "application/gzip", content, DateTimeOffset.UtcNow, ct);
            }
            public ValueTask DisposeAsync() => Db.DisposeAsync();
        }
    }
}
