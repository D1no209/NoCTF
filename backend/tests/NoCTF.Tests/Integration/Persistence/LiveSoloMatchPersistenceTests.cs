using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Infrastructure.LiveSolo.Rounds;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.GameplayFacts.Management;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Domain.Competitions.Events;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Worker.LiveSolo;
using Wolverine;
using Wolverine.Nats;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Infrastructure.Commands.Idempotency;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloMatchPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Roster_countdown_releases_and_scoped_access_use_persisted_current_state(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync(ct);
            var store = fixture.Store(fixture.Db);
            await fixture.PrepareAsync(ct);
            var before = await store.QuestionsAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, fixture.Left.Id, fixture.Now, ct);
            await Assert.That(before).IsNull();
            var notReady = await store.StartCountdownAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id,
                fixture.Owner.Id, fixture.Round.ConcurrencyStamp, fixture.Now), ct);
            await Assert.That(notReady.Failure).IsEqualTo(LiveSoloFailure.NotReady);
            await fixture.StartAsync(ct);
            var first = await store.QuestionsAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, fixture.Left.Id, fixture.Now, ct);
            await Assert.That(first!.Count).IsEqualTo(1);
            var access = new LiveSoloExecutionAccess(fixture.Db);
            var request = new ExecutionScopeAccessRequest(first[0].Id, fixture.Competition.Id, first[0].CompetitionChallengeId,
                fixture.LeftTeam.Id, fixture.Left.Id, ExecutionScopeOperation.Read, fixture.Now);
            await Assert.That(await access.CanAccessAsync(request, ct)).IsTrue();
            await Assert.That(await access.CanAccessAsync(request with { ActorId = fixture.Owner.Id }, ct)).IsFalse();
            await Assert.That(await access.CanAccessAsync(request with { TeamId = fixture.RightTeam.Id }, ct)).IsFalse();
            var unopened = fixture.Round.Questions.Single(x => x.Position == 1);
            await Assert.That(await access.CanAccessAsync(request with { ExecutionScopeId = unopened.Id,
                CompetitionChallengeId = unopened.CompetitionChallengeId }, ct)).IsFalse();
            fixture.Now = fixture.Now.AddSeconds(10);
            await store.TickAsync(fixture.Round.Id, fixture.Round.TimelineRevision, fixture.Now, ct);
            var both = await store.QuestionsAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, fixture.Right.Id, fixture.Now, ct);
            await Assert.That(both!.Count).IsEqualTo(2);
            await Assert.That(both[0].OpenedAt).IsEqualTo(first[0].OpenedAt);
            fixture.Now = first[0].OpenedAt.AddSeconds(31);
            var late = await fixture.Store(fixture.Db).AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id,
                first[0].Id, fixture.Left.Id, "flag{first}", first[0].OpenedAt), ct);
            await Assert.That(late.Failure).IsEqualTo(LiveSoloFailure.DeadlinePassed);
            fixture.Competition.Status = CompetitionStatus.Paused; await fixture.Db.SaveChangesAsync(ct);
            await Assert.That(await access.CanAccessAsync(request with { Now = fixture.Now }, ct)).IsTrue();
            await Assert.That(await access.CanAccessAsync(request with { Operation = ExecutionScopeOperation.Reset, Now = fixture.Now }, ct)).IsFalse();
            fixture.LeftTeam.IsBanned = true; await fixture.Db.SaveChangesAsync(ct);
            await Assert.That(await access.CanAccessAsync(request with { Now = fixture.Now }, ct)).IsFalse();
            await Assert.That(await store.QuestionsAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, fixture.Left.Id, fixture.Now, ct)).IsNull();
            fixture.LeftTeam.IsBanned = false;
            ((LiveSoloCompetitionModeConfiguration)fixture.Competition.ModeConfiguration!).Enabled = false;
            await fixture.Db.SaveChangesAsync(ct);
            await Assert.That(await access.CanAccessAsync(request with { Now = fixture.Now }, ct)).IsFalse();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Concurrent_admissions_share_one_sequence_and_a_pending_earlier_attempt_blocks_later_correct_answers(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var question = fixture.Round.Questions.Single(x => x.Position == 0);
            var accepted = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                var actor = i % 2 == 0 ? fixture.Left.Id : fixture.Right.Id;
                return await fixture.Store(db).AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id,
                    question.Id, actor, "flag{first}", fixture.Now), ct);
            }));
            // Serializable conflicts may exhaust the bounded retry budget; retry only those unaccepted calls.
            for (var i = 0; i < accepted.Length; i++)
                if (accepted[i].Failure == LiveSoloFailure.Conflict)
                {
                    await using var db = new NoCtfDbContext(fixture.Options);
                    accepted[i] = await fixture.Store(db).AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id,
                        question.Id, i % 2 == 0 ? fixture.Left.Id : fixture.Right.Id, "flag{first}", fixture.Now), ct);
                }
            await Assert.That(accepted.All(x => x.Failure is null)).IsTrue();
            await Assert.That(accepted.Select(x => x.Sequence!.Value)).IsEquivalentTo(new long[] { 1, 2, 3, 4 });
            fixture.Db.ChangeTracker.Clear();
            var ordered = await fixture.Db.LiveSoloSubmissions.OrderBy(x => x.AdmissionSequence).ToArrayAsync(ct);
            var pending = await fixture.Db.GameplayFacts.SingleAsync(x => x.Id == ordered[0].GameplayFactId, ct);
            pending.State = GameplayFactState.PlatformFailed;
            await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = fixture.Now.AddSeconds(31);
            await fixture.Store(fixture.Db).TickAsync(fixture.Round.Id, fixture.Round.TimelineRevision, fixture.Now, ct);
            var blocked = await fixture.Db.LiveSoloRounds.SingleAsync(x => x.Id == fixture.Round.Id, ct);
            await Assert.That(blocked.State).IsEqualTo(LiveSoloRoundState.ConfirmingResult);
            var later = await fixture.Db.GameplayFacts.SingleAsync(x => x.Id == ordered[1].GameplayFactId, ct);
            await Assert.That(later.Result).IsEqualTo(GameplayFactResult.Correct);
            pending.State = GameplayFactState.Completed; pending.Result = GameplayFactResult.Wrong;
            await fixture.Db.SaveChangesAsync(ct);
            await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                await fixture.Store(db).ResolveAsync(fixture.Round.Id, fixture.Now.AddSeconds(2), ct);
            }));
            fixture.Db.ChangeTracker.Clear();
            var result = await fixture.Db.LiveSoloRounds.SingleAsync(x => x.Id == fixture.Round.Id, ct);
            var match = await fixture.Db.LiveSoloMatches.SingleAsync(x => x.Id == fixture.Match.Id, ct);
            await Assert.That(result.WinningGameplayFactId).IsEqualTo(later.Id);
            await Assert.That(match.LeftWins + match.RightWins).IsEqualTo(1);
            await Assert.That(match.State).IsEqualTo(LiveSoloMatchState.Completed);
            await Assert.That(await fixture.Db.LiveSoloActiveTeamSlots.CountAsync(ct)).IsEqualTo(0);
            var generic = new GameplayFactManagementStore(fixture.Db, Substitute.For<IPostCommitMessagePublisher>());
            await Assert.That(await generic.QueueDrainAsync(fixture.Competition.Id, question.CompetitionChallengeId, fixture.Now, true, later.Id, ct))
                .IsEqualTo(GameplayFactWorkQueueState.IndependentAdjudicationRequired);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Competition_pause_history_survives_restart_and_scheduler_rebuild_does_not_release_a_stale_round(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var started = fixture.Now;
            void Transition(DateTimeOffset at, CompetitionStatus from, CompetitionStatus to)
            {
                var item = CompetitionEventGeneratedCatalog.Create(CompetitionEventKind.CompetitionLifecycleChanged);
                item.Id = Guid.NewGuid(); item.CompetitionId = fixture.Competition.Id; item.OccurredAt = at;
                item.PreviousCompetitionStatus = from; item.CompetitionStatus = to;
                fixture.Db.CompetitionEvents.Add(item);
            }
            Transition(started.AddSeconds(5), CompetitionStatus.Running, CompetitionStatus.Paused);
            Transition(started.AddSeconds(35), CompetitionStatus.Paused, CompetitionStatus.Running);
            await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = started.AddSeconds(40);
            await using var restarted = new NoCtfDbContext(fixture.Options);
            var store = fixture.Store(restarted);
            var read = await store.FindRoundAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, fixture.Left.Id, false, fixture.Now, ct);
            await Assert.That(read!.ActiveElapsedMilliseconds).IsEqualTo(10_000);
            await Assert.That(await restarted.Set<LiveSoloPauseInterval>().CountAsync(ct)).IsEqualTo(0);
            var schedules = await new LiveSoloScheduleSource(restarted).RebuildAsync(fixture.Now, ct);
            var wakeup = (AdvanceLiveSoloRound)schedules.Single().Message;
            await store.TickAsync(wakeup.RoundId, wakeup.TimelineRevision, fixture.Now, ct);
            var round = await restarted.LiveSoloRounds.Include(x => x.Questions).SingleAsync(ct);
            await Assert.That(round.State).IsEqualTo(LiveSoloRoundState.Running);
            await Assert.That(round.Questions.Single(x => x.Position == 1).OpenedAt).IsEqualTo(fixture.Now);
            await Assert.That(await restarted.Set<LiveSoloPauseInterval>().CountAsync(ct)).IsEqualTo(1);
            var current = round.TimelineRevision;
            await store.TickAsync(round.Id, wakeup.TimelineRevision, fixture.Now.AddSeconds(1), ct);
            await Assert.That(round.TimelineRevision).IsEqualTo(current);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Acquisition_snapshots_ignore_legacy_attempts_and_later_downloads_and_failed_commit_rolls_back_admission(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync(ct);
            var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = "livesolo-test.pdf", FileName = "test.pdf",
                ContentType = "application/pdf", ByteLength = 4, Sha256 = new byte[32], CreatedAt = fixture.Now };
            var attachment = new ChallengeAttachment { Id = Guid.NewGuid(), ChallengeId = fixture.Templates[0].Id, File = file, CreatedAt = fixture.Now };
            fixture.Db.Add(attachment);
            fixture.Db.GameplayFacts.Add(new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = fixture.Entries[0].Id, TeamId = fixture.LeftTeam.Id, State = GameplayFactState.Completed,
                Result = GameplayFactResult.Wrong, OccurredAt = fixture.Now.AddMinutes(-1), UpdatedAt = fixture.Now });
            await fixture.Db.SaveChangesAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var question = fixture.Round.Questions.Single(x => x.Position == 0);
            var command = new LiveSoloAdmission(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id,
                fixture.Left.Id, "flag{first}", fixture.Now);
            var first = await fixture.Store(fixture.Db).AdmitAsync(command, ct);
            var snapshot = await fixture.Db.GameplayFacts.Include(x => x.AcquisitionEvidence).SingleAsync(x => x.Id == first.GameplayFactId, ct);
            await Assert.That(snapshot.AcquisitionEvidence!.Scope).IsEqualTo(FlagAcquisitionScope.FormalStaticExecution);
            await Assert.That(snapshot.AcquisitionEvidence.MissingEvidence).IsEqualTo(GameplayFactFailureCode.StaticFlagWithoutAttachment);
            var download = new AttachmentDownloadGameplayFact { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = fixture.Entries[0].Id, TeamId = fixture.LeftTeam.Id, ActorUserId = fixture.Left.Id,
                State = GameplayFactState.Completed, Result = GameplayFactResult.Applied, ReferenceKind = GameplayFactReferenceKind.Attachment,
                ReferenceId = attachment.Id, OccurredAt = fixture.Now.AddSeconds(1), UpdatedAt = fixture.Now.AddSeconds(1) };
            fixture.Db.GameplayFacts.Add(download); fixture.Db.LiveSoloDownloadEvidences.Add(new() { GameplayFactId = download.Id, RoundQuestionId = question.Id });
            await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = fixture.Now.AddSeconds(2);
            var second = await fixture.Store(fixture.Db).AdmitAsync(command with { Now = fixture.Now }, ct);
            await fixture.Store(fixture.Db).ResolveAsync(fixture.Round.Id, fixture.Now, ct);
            await Assert.That(snapshot.Result).IsEqualTo(GameplayFactResult.Rejected);
            await Assert.That(snapshot.FailureCode).IsEqualTo(GameplayFactFailureCode.StaticFlagWithoutAttachment);
            await Assert.That((await fixture.Db.LiveSoloRounds.SingleAsync(x => x.Id == fixture.Round.Id, ct)).WinningGameplayFactId).IsEqualTo(second.GameplayFactId);
            // A separate preparing fixture avoids trying to admit to an already finished Round.
            await using var rollback = await Fixture.CreateAsync(ct);
            await rollback.PrepareAsync(ct); await rollback.StartAsync(ct);
            var failure = new FailCommit();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>(rollback.Options).AddInterceptors(failure).Options;
            await using var failingDb = new NoCtfDbContext(options);
            failure.Armed = true;
            await Assert.That(async () => await rollback.Store(failingDb).AdmitAsync(new(rollback.Competition.Id, rollback.Match.Id,
                rollback.Round.Id, rollback.Round.Questions[0].Id, rollback.Left.Id, "flag{first}", rollback.Now), ct)).Throws<InvalidOperationException>();
            await Assert.That(await rollback.Db.LiveSoloSubmissions.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await rollback.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        });
    }

    private sealed class FailCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; throw new InvalidOperationException("Injected LiveSolo commit failure"); }
            return ValueTask.FromResult(result);
        }
    }

    [Test, Timeout(300_000)]
    public async Task Parallel_request_replay_is_atomic_and_cannot_be_reused_on_another_match_route_or_payload(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var key = Guid.NewGuid();
            var command = new LiveSoloAdmission(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id,
                fixture.Round.Questions.Single(x => x.Position == 0).Id, fixture.Left.Id, "flag{first}", fixture.Now);
            var parallel = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                return await fixture.Store(db, key, fixture.Left.Id).AdmitAsync(command, ct);
            }));
            await Assert.That(parallel.All(x => x.Failure is null)).IsTrue();
            await Assert.That(parallel.Select(x => x.GameplayFactId).Distinct().Count()).IsEqualTo(1);
            await Assert.That(await fixture.Db.LiveSoloSubmissions.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await fixture.Db.CommandReceipts.CountAsync(ct)).IsEqualTo(1);
            await using var wrongRoute = new NoCtfDbContext(fixture.Options);
            await Assert.That((await fixture.Store(wrongRoute, key, fixture.Left.Id).AdmitAsync(command with { MatchId = Guid.NewGuid() }, ct)).Failure)
                .IsEqualTo(LiveSoloFailure.NotFound);
            await using var wrongPayload = new NoCtfDbContext(fixture.Options);
            await Assert.That(async () => await fixture.Store(wrongPayload, key, fixture.Left.Id).AdmitAsync(command with { Flag = "flag{changed}" }, ct))
                .Throws<RequestReplayConflictException>();
        });
    }
    private sealed record CommandKey(Guid? Key, Guid? ActorId) : IRequestCommandKey;

    [Test, Timeout(300_000)]
    public async Task Historical_runtime_flags_require_the_current_scope_a_successful_start_and_validity_at_admission(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var question = fixture.Round.Questions.Single(x => x.Position == 0);
            PlayerRuntimeInstance Runtime(Guid scope, DateTimeOffset? running) => new() { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = question.CompetitionChallengeId, TeamId = fixture.LeftTeam.Id, ExecutionScopeId = scope,
                RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, AccessMode = RuntimeAccessMode.WsrxOnly,
                State = running is null ? RuntimeState.Failed : RuntimeState.Stopped, CreatedAt = fixture.Now.AddMinutes(-1), RunningAt = running };
            var failed = Runtime(question.Id, null); var otherScope = Runtime(Guid.NewGuid(), fixture.Now.AddSeconds(-30));
            var stopped = Runtime(question.Id, fixture.Now.AddSeconds(-1));
            fixture.Db.RuntimeInstances.AddRange(failed, otherScope, stopped);
            RuntimeInstanceChallengeFlag Flag(PlayerRuntimeInstance runtime, string value) => new() { Id = Guid.NewGuid(),
                CompetitionChallengeId = question.CompetitionChallengeId, TeamId = fixture.LeftTeam.Id, Flag = value,
                FlagSha256 = ManageChallengeFlags.Hash(value), SpecificationKind = SpecificationKind.RuntimeInstance,
                SpecificationId = runtime.Id, CreatedAt = fixture.Now, ValidUntil = fixture.Now.AddSeconds(1) };
            fixture.Db.ChallengeFlags.AddRange(Flag(failed, "flag{never-started}"), Flag(otherScope, "flag{other-round}"), Flag(stopped, "flag{before-reset}"));
            await fixture.Db.SaveChangesAsync(ct);
            var store = fixture.Store(fixture.Db);
            var failedAttempt = await store.AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id,
                fixture.Left.Id, "flag{never-started}", fixture.Now), ct);
            var otherAttempt = await store.AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id,
                fixture.Left.Id, "flag{other-round}", fixture.Now), ct);
            var validAttempt = await store.AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id,
                fixture.Left.Id, "flag{before-reset}", fixture.Now), ct);
            await fixture.Store(fixture.Db).ResolveAsync(fixture.Round.Id, fixture.Now.AddSeconds(2), ct);
            await Assert.That((await fixture.Db.GameplayFacts.SingleAsync(x => x.Id == failedAttempt.GameplayFactId, ct)).Result).IsEqualTo(GameplayFactResult.Wrong);
            await Assert.That((await fixture.Db.GameplayFacts.SingleAsync(x => x.Id == otherAttempt.GameplayFactId, ct)).Result).IsEqualTo(GameplayFactResult.Wrong);
            await Assert.That((await fixture.Db.LiveSoloRounds.SingleAsync(x => x.Id == fixture.Round.Id, ct)).WinningGameplayFactId).IsEqualTo(validAttempt.GameplayFactId);
        });
    }

    [Test, Timeout(300_000), NotInParallel]
    public async Task JetStream_scheduled_wakeups_drive_the_real_round_store_and_stale_revisions_cannot_open_questions(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var clock = new FakeTimeProvider(fixture.Now.AddSeconds(10));
            var probe = new WakeupProbe();
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await nats.StartAsync(ct);
            var subject = "noctf.test.livesolo." + Guid.NewGuid().ToString("N");
            var stream = "NOCTF_LIVESOLO_" + Guid.NewGuid().ToString("N");
            using var host = Host.CreateDefaultBuilder().ConfigureServices(services =>
            {
                services.AddSingleton(probe); services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton(fixture.Options); services.AddScoped<NoCtfDbContext>();
                services.AddSingleton(fixture.media);
                services.AddSingleton<IChallengeRuntimeTemplateCatalog, ChallengeRuntimeTemplateCatalog>();
                services.AddSingleton(Substitute.For<IRuntimePlacementPolicy>());
                services.AddSingleton(Substitute.For<IPerTeamRuntimeFlagStore>());
                services.AddSingleton(Substitute.For<IPostCommitMessagePublisher>());
                services.AddScoped<IScopedRuntimeControl, ScopedRuntimeControl>();
                services.AddScoped<ILiveSoloRuntimePreparation, LiveSoloRuntimePreparation>();
                services.AddScoped<ICompetitionModerationAuthorizer, CompetitionModerationAuthorizer>();
                services.AddScoped<ILiveSoloMatchStore, LiveSoloMatchStore>();
                services.AddTransient<LiveSoloRoundMessageHandler>();
            }).UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType(typeof(WakeupProbeHandler));
                options.UseNats($"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}").AutoProvision().UseJetStream(_ => { })
                    .DefineWorkQueueStream(stream, config => config.WithSubjects(subject, subject + ".scheduled").EnableScheduledDelivery(), subject);
                options.ListenToNatsSubject(subject).UseJetStream(stream, "livesolo-test");
                options.PublishMessage<AdvanceLiveSoloRound>().ToNatsSubject(subject).UseJetStream(stream);
            }).Build();
            await host.StartAsync(ct);
            try
            {
                var bus = host.Services.GetRequiredService<IMessageBus>();
                var stale = new AdvanceLiveSoloRound(fixture.Round.Id, fixture.Round.TimelineRevision - 1, clock.GetUtcNow());
                await bus.PublishAsync(stale);
                await probe.Received.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                await using var read = new NoCtfDbContext(fixture.Options);
                await Assert.That(await read.LiveSoloRoundQuestions.Where(x => x.RoundId == fixture.Round.Id && x.OpenedAt != null).CountAsync(ct)).IsEqualTo(1);
                probe.Received = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var current = stale with { TimelineRevision = fixture.Round.TimelineRevision };
                await bus.ScheduleAsync(current, DateTimeOffset.UtcNow.AddSeconds(1));
                await probe.Received.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                await Assert.That(await read.LiveSoloRoundQuestions.Where(x => x.RoundId == fixture.Round.Id && x.OpenedAt != null).CountAsync(ct)).IsEqualTo(2);
            }
            finally { await host.StopAsync(ct); }
        });
    }

    public sealed class WakeupProbe { public TaskCompletionSource Received { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public sealed class WakeupProbeHandler(LiveSoloRoundMessageHandler handler, WakeupProbe probe)
    {
        public async Task Handle(AdvanceLiveSoloRound message, CancellationToken ct)
        {
            await handler.Handle(message, ct); probe.Received.TrySetResult();
        }
    }

    internal sealed class Fixture(PostgreSqlContainer postgres, DbContextOptions<NoCtfDbContext> options, NoCtfDbContext db) : IAsyncDisposable
    {
        public DbContextOptions<NoCtfDbContext> Options { get; } = options;
        public NoCtfDbContext Db { get; } = db;
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public User Owner { get; private set; } = null!;
        public User Left { get; private set; } = null!;
        public User Right { get; private set; } = null!;
        public Team LeftTeam { get; private set; } = null!;
        public Team RightTeam { get; private set; } = null!;
        public LiveSoloCompetition Competition { get; private set; } = null!;
        public LiveSoloChallenge[] Templates { get; private set; } = [];
        public LiveSoloCompetitionChallenge[] Entries { get; private set; } = [];
        public LiveSoloMatchView Match { get; private set; } = null!;
        public LiveSoloRound Round { get; private set; } = null!;
        public readonly ILiveSoloMediaGateway media = Substitute.For<ILiveSoloMediaGateway>();

        public static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = new Fixture(postgres, options, new(options));
            await fixture.Db.Database.EnsureCreatedAsync(ct);
            User User(string name) => new() { Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.test", PasswordHash = "unused",
                AccountStatus = UserAccountStatus.Active, CreatedAt = fixture.Now };
            fixture.Owner = User("judge"); fixture.Left = User("left"); fixture.Right = User("right");
            fixture.Competition = new() { Id = Guid.NewGuid(), OwnerId = fixture.Owner.Id, Title = "LiveSolo Round test",
                StartAt = fixture.Now.AddHours(-1), EndAt = fixture.Now.AddHours(1), CreatedAt = fixture.Now, UpdatedAt = fixture.Now,
                Status = CompetitionStatus.Running, FlagDerivationSecret = new byte[32], ModeConfiguration = new LiveSoloCompetitionModeConfiguration {
                    Enabled = true, RequiredWins = 1, QuestionIntervalSeconds = 10, RoundLimitSeconds = 30 } };
            Team Team(User user, char key) => new() { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, Name = user.UserName,
                CaptainId = user.Id, MemberIds = [user.Id], InvitationToken = new string(key, 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now };
            fixture.LeftTeam = Team(fixture.Left, 'l'); fixture.RightTeam = Team(fixture.Right, 'r');
            fixture.Templates = Enumerable.Range(0, 2).Select(i => new LiveSoloChallenge { Id = Guid.NewGuid(), OwnerId = fixture.Owner.Id,
                Title = "Question " + i, Direction = "Web", Description = "secret body " + i, CreatedAt = fixture.Now, UpdatedAt = fixture.Now,
                Definition = new LiveSoloChallengeDefinition() }).ToArray();
            fixture.Entries = fixture.Templates.Select((x, i) => new LiveSoloCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
                ChallengeId = x.Id, Order = i, Rules = new LiveSoloCompetitionChallengeRules(), UpdatedAt = fixture.Now }).ToArray();
            fixture.Db.Users.AddRange(fixture.Owner, fixture.Left, fixture.Right); fixture.Db.Competitions.Add(fixture.Competition);
            fixture.Db.Teams.AddRange(fixture.LeftTeam, fixture.RightTeam); fixture.Db.Challenges.AddRange(fixture.Templates);
            fixture.Db.CompetitionChallenges.AddRange(fixture.Entries);
            fixture.Db.ChallengeFlags.AddRange(fixture.Templates.Select((x, i) => new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = x.Id,
                Flag = i == 0 ? "flag{first}" : "flag{second}", FlagSha256 = ManageChallengeFlags.Hash(i == 0 ? "flag{first}" : "flag{second}"), CreatedAt = fixture.Now }));
            await fixture.Db.SaveChangesAsync(ct);
            fixture.media.CheckAsync(ct).Returns(new LiveSoloMediaReadiness(true, true, true));
            fixture.media.ObserveAsync(Arg.Any<string>(), ct).Returns(new LiveSoloRoomObservation([
                new("left-screen", LiveSoloScreenState.Sharing, "track-left", fixture.Now),
                new("right-screen", LiveSoloScreenState.Sharing, "track-right", fixture.Now)]));
            return fixture;
        }

        public LiveSoloMatchStore Store(NoCtfDbContext context, Guid? requestKey = null, Guid? actorId = null)
        {
            var messages = Substitute.For<IPostCommitMessagePublisher>();
            var runtime = new ScopedRuntimeControl(context, new ChallengeRuntimeTemplateCatalog(), Substitute.For<IRuntimePlacementPolicy>(),
                Substitute.For<IPerTeamRuntimeFlagStore>(), messages);
            var clock = new FakeTimeProvider(Now);
            return new(context, new CompetitionModerationAuthorizer(context), media, new LiveSoloRuntimePreparation(context, runtime), messages,
                new TransactionalRequestReplay(context, new CommandKey(requestKey, actorId), clock), clock);
        }

        public async Task PrepareAsync(CancellationToken ct)
        {
            var store = Store(Db);
            var group = await store.SaveGroupAsync(Competition.Id, Owner.Id, new(null, "Round group", false, null,
                Entries.Select(x => new LiveSoloQuestionGroupEntry(x.Id, null)).ToArray(), null), Now, ct);
            await Assert.That(group.Failure).IsNull();
            var created = await store.CreateAsync(new(Competition.Id, Owner.Id, LeftTeam.Id, RightTeam.Id, null, Now), ct);
            await Assert.That(created.Failure).IsNull(); Match = created.Match!;
            var left = await store.LockRosterAsync(new(Competition.Id, Match.Id, Left.Id, LeftTeam.Id, Match.ConcurrencyStamp, LeftTeam.MemberIds, Now), ct);
            await Assert.That(left.Failure).IsNull(); Match = left.Match!;
            var right = await store.LockRosterAsync(new(Competition.Id, Match.Id, Right.Id, RightTeam.Id, Match.ConcurrencyStamp, [Right.Id], Now), ct);
            await Assert.That(right.Failure).IsNull(); Match = right.Match!;
            var prepared = await store.PrepareRoundAsync(new(Competition.Id, Match.Id, Owner.Id, Match.ConcurrencyStamp, group.Group!.Id, Now), ct);
            await Assert.That(prepared.Failure).IsNull();
            Round = await Db.LiveSoloRounds.Include(x => x.Questions).SingleAsync(x => x.Id == prepared.Round!.Id, ct);
            Match = (await store.FindAsync(Competition.Id, Match.Id, Owner.Id, true, Now, ct))!;
            var participants = Match.Rosters.SelectMany(roster => roster.UserIds.Select(id => new LiveSoloMediaParticipant {
                UserId = id, TeamId = roster.TeamId, Side = roster.TeamId == LeftTeam.Id ? LiveSoloSide.Left : LiveSoloSide.Right,
                Identity = id == Left.Id ? "left-screen" : id == Right.Id ? "right-screen" : id.ToString("N"), ObservedAt = Now })).ToList();
            Db.LiveSoloMediaSessions.Add(new() { Id = Guid.NewGuid(), MatchId = Match.Id, Generation = Guid.NewGuid(), RoomIdentity = Guid.NewGuid().ToString("N"),
                State = LiveSoloMediaState.Ready, CreatedAt = Now, Participants = participants });
            media.ObserveAsync(Arg.Any<string>(), ct).Returns(new LiveSoloRoomObservation(participants.Select(x =>
                new LiveSoloObservedScreen(x.Identity, LiveSoloScreenState.Sharing, "track-" + x.Identity, Now)).ToArray()));
            await Db.SaveChangesAsync(ct);
        }

        public async Task StartAsync(CancellationToken ct)
        {
            var store = Store(Db);
            var left = await store.ConfirmReadyAsync(Competition.Id, Match.Id, Left.Id, Match.ConcurrencyStamp, Now, ct);
            await Assert.That(left.Failure).IsNull(); Match = left.Match!;
            var right = await store.ConfirmReadyAsync(Competition.Id, Match.Id, Right.Id, Match.ConcurrencyStamp, Now, ct);
            await Assert.That(right.Failure).IsNull(); Match = right.Match!;
            var countdown = await store.StartCountdownAsync(new(Competition.Id, Match.Id, Round.Id, Owner.Id, Round.ConcurrencyStamp, Now), ct);
            await Assert.That(countdown.Failure).IsNull();
            Now = Now.AddSeconds(Round.CountdownSeconds);
            await store.TickAsync(Round.Id, countdown.Round!.TimelineRevision, Now, ct);
            await Assert.That(Round.State).IsEqualTo(LiveSoloRoundState.Running);
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await postgres.DisposeAsync(); }
    }
}
