using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentStorage;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.Application.Challenges.Actions;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Hosting.Messaging;
using NoCTF.Hosting;
using NoCTF.Infrastructure.Commands.Idempotency;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.LiveSolo.Templates;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Runtime.Placement;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Unit.Gameplay;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloFixPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Active_fix_round_protects_its_consumed_capabilities_and_shared_construction(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        await using var session = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var templateId = await session.Db.CompetitionChallenges.Select(value => value.ChallengeId).SingleAsync(ct);
        var gate = new LiveSoloMaterialMutationGate(session.Db);
        await using var transaction = await session.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var template = await session.Db.Challenges.SingleAsync(value => value.Id == templateId, ct);
        var previousStamp = template.ConcurrencyStamp;
        await gate.RequireMutableAsync(new(templateId, null, [ChallengeCapabilityKind.Break]), ct);
        await gate.RequireMutableAsync(new(templateId, null, [ChallengeCapabilityKind.Control]), ct);
        await Assert.That(template.ConcurrencyStamp == previousStamp).IsFalse();
        var consumed = await Assert.That(() => gate.RequireMutableAsync(new(templateId, null, [ChallengeCapabilityKind.Check]), ct))
            .Throws<ChallengeMaterialMutationException>();
        await Assert.That(consumed!.Failure).IsEqualTo(ChallengeMaterialMutationFailure.ActiveExecutionScope);
        await Assert.That(() => gate.RequireMutableAsync(new(templateId, null, [], RuntimeChanged: true), ct)).Throws<ChallengeMaterialMutationException>();
        await Assert.That(() => gate.RequireMutableAsync(new(templateId, null), ct)).Throws<ChallengeMaterialMutationException>();
        await session.Db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    [Test, Timeout(300_000)]
    public async Task Fix_uploads_have_real_scope_clean_targets_and_idempotent_admission_order(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        var key = Guid.NewGuid();
        await using var session = fixture.Open(fixture.LeftId, key);
        var request = fixture.Request(fixture.LeftId);
        var target = await session.Targets.RequestAsync(request, ct);
        await Assert.That(target.Failure).IsNull();
        var repeated = await session.Targets.RequestAsync(request, ct);
        await Assert.That(repeated.RuntimeInstanceId).IsEqualTo(target.RuntimeInstanceId);
        var questions = await session.Matches.QuestionsAsync(fixture.CompetitionId, fixture.MatchId, fixture.RoundId, fixture.LeftId, fixture.Now, ct);
        var question = questions!.Single();
        await Assert.That(question.CompletionKind).IsEqualTo(LiveSoloCompletionKind.FixAndCheck);
        await Assert.That(question.AvailableActions.Single(action => action.Kind == ChallengeActionKind.SubmitFix).Enabled).IsTrue();
        await Assert.That(question.AvailableActions.Any(action => action.Kind == ChallengeActionKind.SubmitFlag)).IsFalse();
        await Assert.That((await session.Matches.AdmitAsync(new(fixture.CompetitionId, fixture.MatchId, fixture.RoundId, fixture.QuestionId,
            fixture.LeftId, "flag{ineligible}", fixture.Now), ct)).Failure).IsEqualTo(LiveSoloFailure.SubmissionActionUnsupported);
        await using var foreign = fixture.Open(fixture.RightId, Guid.NewGuid());
        var denied = await foreign.UploadAsync(target.RuntimeInstanceId!.Value, ct);
        await Assert.That(denied.Failure).IsEqualTo(PatchUploadFailureCode.PatchUploadNotAvailable);
        await using var upload = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var accepted = await upload.UploadAsync(target.RuntimeInstanceId.Value, ct);
        await Assert.That(accepted.Failure).IsNull();
        await Assert.That(accepted.Admission!.Sequence).IsEqualTo(1L);
        await Assert.That((await upload.UploadAsync(target.RuntimeInstanceId.Value, ct)).Upload!.GameplayFactId).IsEqualTo(accepted.Upload!.GameplayFactId);
        await using var next = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var clean = await next.Targets.RequestAsync(request, ct);
        await Assert.That(clean.Failure).IsNull();
        await Assert.That(clean.RuntimeInstanceId).IsNotEqualTo(target.RuntimeInstanceId);
        await using var secondUpload = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var second = await secondUpload.UploadAsync(clean.RuntimeInstanceId!.Value, ct);
        await Assert.That(second.Failure).IsNull();
        await Assert.That(second.Admission!.Sequence).IsEqualTo(2L);
        await using var read = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var resumed = await read.Targets.ReadAsync(request, ct);
        await Assert.That(resumed.AdmissionSequence).IsEqualTo(2L);
        await Assert.That(resumed.GameplayFactId).IsEqualTo(second.Upload!.GameplayFactId);
        await Assert.That(await read.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(2);
        var instances = await read.Db.RuntimeInstances.AsSplitQuery().ToArrayAsync(ct);
        await Assert.That(instances.All(instance => instance.ExecutionScopeId == null && instance.Purpose == RuntimePurpose.ScopedFixTarget)).IsTrue();
        await Assert.That(instances.Select(instance => instance.GameplayFactId).Distinct().Count()).IsEqualTo(2);
    }

    [Test, Timeout(300_000)]
    public async Task Deadline_blocks_new_uploads_and_targets_but_keeps_accepted_execution_until_its_deadline(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        await using var session = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var target = await session.Targets.RequestAsync(fixture.Request(fixture.LeftId), ct);
        await using var upload = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var accepted = await upload.UploadAsync(target.RuntimeInstanceId!.Value, ct);
        await Assert.That(accepted.Failure).IsNull();
        await using var queued = fixture.Open(fixture.RightId, Guid.NewGuid());
        var unused = await queued.Targets.RequestAsync(fixture.Request(fixture.RightId), ct);
        await Assert.That(unused.Failure).IsNull();
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        await using var after = fixture.Open(fixture.LeftId, Guid.NewGuid());
        await Assert.That((await after.Targets.RequestAsync(fixture.Request(fixture.LeftId), ct)).Failure).IsNotNull();
        var active = await after.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == target.RuntimeInstanceId, ct);
        await Assert.That(await LiveSoloFixTargetValidity.CanProvisionAsync(after.Db, active, fixture.Now, ct)).IsTrue();
        var expired = await after.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == unused.RuntimeInstanceId, ct);
        await Assert.That(await LiveSoloFixTargetValidity.CanProvisionAsync(after.Db, expired, fixture.Now, ct)).IsFalse();
        await using var lateUpload = fixture.Open(fixture.RightId, Guid.NewGuid());
        await Assert.That((await lateUpload.UploadAsync(unused.RuntimeInstanceId!.Value, ct)).Failure).IsNotNull();
        await after.Matches.ResolveAsync(fixture.RoundId, fixture.Now, ct);
        after.Db.ChangeTracker.Clear();
        await Assert.That((await after.Db.LiveSoloRounds.SingleAsync(ct)).State).IsEqualTo(LiveSoloRoundState.Running);
        await Assert.That((await after.Db.GameplayFacts.AsSplitQuery().SingleAsync(ct)).State).IsEqualTo(GameplayFactState.Pending);
        var round = await after.Db.LiveSoloRounds.SingleAsync(ct); round.State = LiveSoloRoundState.Canceled; round.EndedAt = fixture.Now;
        await after.Db.SaveChangesAsync(ct);
        await Assert.That(await LiveSoloFixTargetValidity.CanProvisionAsync(after.Db, active, fixture.Now, ct)).IsFalse();
        await after.Preparation.StopRoundAsync(fixture.RoundId, fixture.Now, ct);
        after.Db.ChangeTracker.Clear();
        await Assert.That(await after.Db.RuntimeInstances.CountAsync(value => value.State == RuntimeState.Stopping, ct)).IsEqualTo(2);
        var rejected = await after.Db.GameplayFacts.AsSplitQuery().SingleAsync(ct);
        await Assert.That(rejected.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(rejected.FailureCode).IsEqualTo(GameplayFactFailureCode.RoundOutOfRange);
    }

    [Test, Timeout(300_000)]
    public async Task Earlier_fix_waiting_for_cleanup_blocks_a_later_completed_fix(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        await using var left = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var firstTarget = await left.Targets.RequestAsync(fixture.Request(fixture.LeftId), ct);
        var first = await left.UploadAsync(firstTarget.RuntimeInstanceId!.Value, ct);
        await using var right = fixture.Open(fixture.RightId, Guid.NewGuid());
        var secondTarget = await right.Targets.RequestAsync(fixture.Request(fixture.RightId), ct);
        var second = await right.UploadAsync(secondTarget.RuntimeInstanceId!.Value, ct);
        await Assert.That(first.Admission!.Sequence).IsEqualTo(1L);
        await Assert.That(second.Admission!.Sequence).IsEqualTo(2L);
        await using var resolve = fixture.Open(fixture.LeftId, Guid.NewGuid());
        var pending = await resolve.Db.GameplayFacts.AsSplitQuery().SingleAsync(value => value.Id == first.Upload!.GameplayFactId, ct);
        pending.State = GameplayFactState.Processing;
        pending.Result = GameplayFactResult.Correct;
        pending.FixExecution!.Phase = FixExecutionPhase.Finalizing;
        var completed = await resolve.Db.GameplayFacts.AsSplitQuery().SingleAsync(value => value.Id == second.Upload!.GameplayFactId, ct);
        completed.State = GameplayFactState.Completed;
        completed.Result = GameplayFactResult.Correct;
        completed.FixExecution!.Phase = FixExecutionPhase.Finished;
        await resolve.Db.SaveChangesAsync(ct);
        await resolve.Matches.ResolveAsync(fixture.RoundId, fixture.Now, ct);
        resolve.Db.ChangeTracker.Clear();
        var waiting = await resolve.Db.LiveSoloRounds.SingleAsync(ct);
        await Assert.That(waiting.State).IsEqualTo(LiveSoloRoundState.Running);
        await Assert.That(waiting.LastResolvedSequence).IsEqualTo(0L);
        pending = await resolve.Db.GameplayFacts.AsSplitQuery().SingleAsync(value => value.Id == first.Upload!.GameplayFactId, ct);
        pending.State = GameplayFactState.Completed;
        pending.FixExecution!.Phase = FixExecutionPhase.Finished;
        await resolve.Db.SaveChangesAsync(ct);
        await resolve.Matches.ResolveAsync(fixture.RoundId, fixture.Now, ct);
        resolve.Db.ChangeTracker.Clear();
        var winner = await resolve.Db.LiveSoloRounds.SingleAsync(ct);
        var firstTeam = await resolve.Db.GameplayFacts.Where(value => value.Id == first.Upload!.GameplayFactId).Select(value => value.TeamId).SingleAsync(ct);
        await Assert.That(winner.State).IsEqualTo(LiveSoloRoundState.Won);
        await Assert.That(winner.WinnerTeamId).IsEqualTo(firstTeam);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_solo_fix_{Guid.NewGuid():N}").Build();
        private readonly IContainer nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.14.7-alpine@sha256:4063edae0717ba5f7501bfde75f97fd9b57f5b93597b92c70b6a6fbbf6a74e06")
            .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
        private readonly string directory = Path.Combine(AppContext.BaseDirectory, "live-solo-fix-data", Guid.NewGuid().ToString("N"));
        private IHost? messaging;
        public DbContextOptions<NoCtfDbContext> Options { get; private set; } = null!;
        public IStore Objects { get; private set; } = null!;
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public Guid MatchId { get; } = Guid.NewGuid();
        public Guid RoundId { get; } = Guid.NewGuid();
        public Guid QuestionId { get; } = Guid.NewGuid();
        public Guid LeftId { get; } = Guid.NewGuid();
        public Guid RightId { get; } = Guid.NewGuid();
        public ExplicitClock Clock { get; } = new();
        public DateTimeOffset Now => Clock.GetUtcNow();
        public LiveSoloResourceRequest Request(Guid actorId) => new(CompetitionId, MatchId, RoundId, QuestionId, actorId, Now);

        public static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var fixture = new Fixture();
            try
            {
                await Task.WhenAll(fixture.postgres.StartAsync(ct), fixture.nats.StartAsync(ct));
                fixture.Options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(fixture.postgres.GetConnectionString()).Options;
                Directory.CreateDirectory(fixture.directory); fixture.Objects = StorageFactory.Disk(Path.Combine(fixture.directory, "objects"));
                var subject = NatsSubjects.Subject(WorkerQueue.Background);
                fixture.messaging = Host.CreateDefaultBuilder().UseWolverine(options =>
                {
                    options.Discovery.DisableConventionalDiscovery();
                    options.UseNats($"nats://{fixture.nats.Hostname}:{fixture.nats.GetMappedPublicPort(4222)}").AutoProvision().UseJetStream(_ => { })
                        .DefineWorkQueueStream(NatsSubjects.BackgroundStream, stream => stream.WithSubject(subject), subject);
                    options.PublishMessage<DispatchRuntime>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                    options.PublishMessage<StartFixExecution>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                    options.PublishMessage<StopRuntime>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                    options.PublishMessage<GameplayFactStateChanged>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                    options.PublishMessage<CleanupFile>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                    options.PublishMessage<NoCTF.Application.LiveSolo.Realtime.LiveSoloMatchChanged>().ToNatsSubject(subject).UseJetStream(NatsSubjects.BackgroundStream);
                }).Build();
                await fixture.messaging.StartAsync(ct); await fixture.SeedAsync(ct); return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        private async Task SeedAsync(CancellationToken ct)
        {
            await using var db = new NoCtfDbContext(Options); await db.Database.EnsureCreatedAsync(ct);
            var entryId = Guid.NewGuid(); var templateId = Guid.NewGuid(); var groupId = Guid.NewGuid();
            var leftTeam = Guid.NewGuid(); var rightTeam = Guid.NewGuid();
            foreach (var userId in new[] { LeftId, RightId })
                db.Users.Add(new() { Id = userId, UserName = userId.ToString("N"), NormalizedUserName = userId.ToString("N").ToUpperInvariant(),
                    Email = $"{userId:N}@example.test", NormalizedEmail = $"{userId:N}@EXAMPLE.TEST", AccountStatus = UserAccountStatus.Active });
            db.Competitions.Add(new LiveSoloCompetition { Id = CompetitionId, OwnerId = LeftId, Title = "Fix duel", Status = CompetitionStatus.Running,
                StartAt = Now.AddMinutes(-1), EndAt = Now.AddHours(1), ModeConfiguration = new LiveSoloCompetitionModeConfiguration { Enabled = true } });
            db.Challenges.Add(new() { Id = templateId, OwnerId = LeftId, Title = "Repair", Definition = ChallengeCapabilityTests.Repairable() });
            db.CompetitionChallenges.Add(new LiveSoloCompetitionChallenge { Id = entryId, CompetitionId = CompetitionId, ChallengeId = templateId,
                IsPublished = true, Rules = new LiveSoloCompetitionChallengeRules { CompletionKind = LiveSoloCompletionKind.FixAndCheck } });
            db.Teams.AddRange(new Team { Id = leftTeam, CompetitionId = CompetitionId, CaptainId = LeftId, MemberIds = [LeftId], Name = "Left", NormalizedName = "LEFT", InvitationToken = new string('l', 32), RegistrationStatus = TeamRegistrationStatus.Approved },
                new Team { Id = rightTeam, CompetitionId = CompetitionId, CaptainId = RightId, MemberIds = [RightId], Name = "Right", NormalizedName = "RIGHT", InvitationToken = new string('r', 32), RegistrationStatus = TeamRegistrationStatus.Approved });
            db.LiveSoloQuestionGroups.Add(new() { Id = groupId, CompetitionId = CompetitionId, Name = "Repairs", Items = [new() { CompetitionChallengeId = entryId }] });
            db.LiveSoloMatches.Add(new() { Id = MatchId, CompetitionId = CompetitionId, State = LiveSoloMatchState.Running, CreatedAt = Now.AddMinutes(-1),
                Slots = [new() { Side = LiveSoloSide.Left, TeamId = leftTeam, RosterLockedAt = Now }, new() { Side = LiveSoloSide.Right, TeamId = rightTeam, RosterLockedAt = Now }],
                Roster = [new() { TeamId = leftTeam, UserId = LeftId }, new() { TeamId = rightTeam, UserId = RightId }] });
            await db.SaveChangesAsync(ct);
            db.LiveSoloRounds.Add(new() { Id = RoundId, MatchId = MatchId, QuestionGroupId = groupId, State = LiveSoloRoundState.Running,
                Number = 1, CreatedAt = Now, StartedAt = Now, LimitSeconds = 60, PlatformStreamingEnabled = false,
                Questions = [new() { Id = QuestionId, CompetitionChallengeId = entryId, OpenedAt = Now, Readiness = LiveSoloQuestionReadiness.Ready,
                    OpenedContent = new() { Title = "Repair", Direction = "Web", CompletionKind = LiveSoloCompletionKind.FixAndCheck } }] });
            await db.SaveChangesAsync(ct);
            (await db.LiveSoloMatches.SingleAsync(ct)).CurrentRoundId = RoundId; await db.SaveChangesAsync(ct);
        }
        public Session Open(Guid actorId, Guid key) => new(this, actorId, key, messaging!.Services);
        public async ValueTask DisposeAsync()
        {
            if (messaging is not null) { await messaging.StopAsync(); messaging.Dispose(); }
            Objects?.Dispose(); await nats.DisposeAsync(); await postgres.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        public sealed class Session : IAsyncDisposable
        {
            private readonly Fixture fixture; private readonly Guid actorId; private readonly SubmitLiveSoloFix submit;
            private readonly byte[] archiveBytes = CreateArchive();
            public NoCtfDbContext Db { get; }
            public LiveSoloFixTargetStore Targets { get; }
            public LiveSoloMatchStore Matches { get; }
            public LiveSoloRuntimePreparation Preparation { get; }
            public Session(Fixture fixture, Guid actorId, Guid key, IServiceProvider services)
            {
                this.fixture = fixture; this.actorId = actorId; Db = new(fixture.Options);
                var publisher = new WolverinePostCommitMessagePublisher(services.GetRequiredService<IMessageBus>(), Db,
                    services.GetRequiredService<ILogger<WolverinePostCommitMessagePublisher>>());
                var replay = new TransactionalRequestReplay(Db, new CommandKey(key, actorId), fixture.Clock);
                var access = new LiveSoloExecutionAccess(Db);
                var placement = new ConfiguredRuntimePlacementPolicy(Microsoft.Extensions.Options.Options.Create(new RuntimePlacementOptions()));
                var control = new ScopedRuntimeControl(Db, new ChallengeRuntimeTemplateCatalog(), placement, new PerTeamRuntimeFlagStore(Db), publisher, access, fixture.Clock, replay);
                Preparation = new(Db, control, publisher);
                var disabledMedia = new UnconfiguredLiveSoloMediaGateway();
                Matches = new(Db, new CompetitionModerationAuthorizer(Db), disabledMedia, disabledMedia, Preparation, publisher, replay, fixture.Clock);
                Targets = new(Db, access, placement, publisher, fixture.Clock, replay);
                var registry = new ManagedFileUploadRegistry(Db, publisher, services.GetRequiredService<ILogger<ManagedFileUploadRegistry>>());
                submit = new(Matches, new ManagedFileUploads(registry, fixture.Objects), replay);
            }
            public async Task<LiveSoloFixUploadResult> UploadAsync(Guid targetId, CancellationToken ct)
            {
                using var archive = new MemoryStream(archiveBytes, writable: false);
                return await submit.ExecuteAsync(fixture.Request(actorId), targetId, "fix.tar.gz", "application/gzip", archive, ct);
            }
            private static byte[] CreateArchive()
            {
                using var archive = new MemoryStream();
                using (var gzip = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
                using (var tar = new TarWriter(gzip, leaveOpen: true))
                using (var script = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nset -eu\nprintf repaired > /repair-state\n")))
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = script });
                return archive.ToArray();
            }
            public ValueTask DisposeAsync() => Db.DisposeAsync();
        }
        private sealed record CommandKey(Guid? Key, Guid? ActorId) : IRequestCommandKey;
    }
    private sealed class ExplicitClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
