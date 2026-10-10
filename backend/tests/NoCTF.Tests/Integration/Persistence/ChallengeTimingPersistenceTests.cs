using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Domain.Runtime;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.Intake;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Common;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Infrastructure.Challenges.Configuration;
using NoCTF.GameModes.Registration;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Challenges.Timing;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Notifications;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Domain.Teams;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTimingPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task A_cached_payload_without_timing_metadata_is_invalidated_and_rebuilt(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
        var provider = services.GetRequiredService<IFusionCacheProvider>();
        var cache = new FusionLeaderboardCache(f.Db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            Substitute.For<ILeaderboardRefreshPublisher>(), provider);
        var projection = await cache.CreateScoreboardAsync(f.Competition.Id, f.Now, ct);
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new CachedScoreboardProjection(projection!), CachedScoreboardJsonContext.Default.CachedScoreboardProjection))!.AsObject();
        json.Remove("timingRevisions");
        var incomplete = JsonSerializer.Deserialize(json.ToJsonString(), CachedScoreboardJsonContext.Default.CachedScoreboardProjection)!;
        await provider.GetCache(NoCtfCacheNames.Leaderboards).SetAsync($"scoreboard:v3:{f.Competition.Id:N}", incomplete, token: ct);
        var current = await cache.GetScoreboardAsync(f.Competition.Id, ct);
        await Assert.That(current).IsNotNull();
        var rebuilt = await provider.GetCache(NoCtfCacheNames.Leaderboards).GetOrDefaultAsync<CachedScoreboardProjection?>($"scoreboard:v3:{f.Competition.Id:N}", null, token: ct);
        await Assert.That(rebuilt!.TimingRevisions.Count).IsEqualTo(1);
    });

    [Test, Timeout(300_000)]
    public Task First_evaluation_excludes_time_invalid_history_from_the_attempt_budget(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct); f.Challenge.IsPublished = true;
        ((CtfCompetitionChallengeRules)f.Challenge.Rules!).MaxFlagAttempts = 1;
        var team = new Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "Budget team", CaptainId = f.Competition.OwnerId,
            MemberIds = [f.Competition.OwnerId], InvitationToken = new string('h', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now };
        f.Db.Add(team); var templateId = f.Challenge.ChallengeId;
        f.Db.Add(new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = templateId, Flag = "flag{budget}",
            FlagSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash("flag{budget}"), CreatedAt = f.Now.AddHours(-1) });
        f.Db.Add(new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id,
            TeamId = team.Id, Value = "wrong", State = GameplayFactState.Completed, Result = GameplayFactResult.Wrong,
            OccurredAt = f.Now.AddMinutes(-10), UpdatedAt = f.Now.AddMinutes(-10) });
        var id = Guid.NewGuid(); f.Db.Add(new FlagAttemptGameplayFact { Id = id, CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, TeamId = team.Id, ActorUserId = f.Competition.OwnerId,
            Value = "flag{budget}", ValueSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash("flag{budget}"),
            State = GameplayFactState.Queued, OccurredAt = f.Now.AddMinutes(-1), UpdatedAt = f.Now.AddMinutes(-1) });
        await f.Db.SaveChangesAsync(ct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(f.Now.AddMinutes(-5)), f.Now), ct);
        var processor = new GameplayFactProcessor(f.Db, new NoCTF.GameModes.GameplayFact.GameModeGameplayFactEvaluatorCatalog(), new GameModeGameplayFactAdmissionPolicy(),
            Substitute.For<IPostCommitMessagePublisher>(), Substitute.For<ILeaderboardSnapshotFactory>());
        await processor.ProcessAsync(id, ct);
        await Assert.That(await f.Db.GameplayFacts.Where(x => x.Id == id).Select(x => x.Result).SingleAsync(ct)).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That((await new FlagAttemptStateReader(f.Db).ReadAsync(f.Competition.Id, f.Challenge.Id, team.Id, GameMode.Ctf, CompetitionStatus.Running, ct))!.Accepted).IsEqualTo(1);
    });

    [Test, Timeout(300_000)]
    public Task A_concurrent_policy_save_cannot_be_overwritten_by_an_inflight_old_batch(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct); var at = f.Now.AddMinutes(-10);
        var id = Guid.NewGuid(); f.Db.Add(new FlagAttemptGameplayFact { Id = id, CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, Value = "synthetic", Result = GameplayFactResult.Correct,
            State = GameplayFactState.Completed, OccurredAt = at, UpdatedAt = at }); await f.Db.SaveChangesAsync(ct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at), f.Now), ct);
        var oldRevision = f.Challenge.TimingRevision; var gate = new BatchReadGate();
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(f.Db.Database.GetConnectionString()).UseSnakeCaseNamingConvention().AddInterceptors(gate).Options;
        await using var oldDb = new NoCtfDbContext(options);
        var oldMessages = Substitute.For<IPostCommitMessagePublisher>();
        var oldStore = new ChallengeTimingStore(oldDb, oldMessages,
            new ChallengeManagementStore(oldDb, oldMessages, Substitute.For<IChallengeRuntimeTemplateCatalog>()), NullCompetitionEventRecorder.Instance);
        var pending = oldStore.RecalculateAsync(new(f.Challenge.Id, oldRevision), f.Now, ct);
        await gate.Started.Task.WaitAsync(ct);
        Guid currentRevision;
        try
        {
            await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, f.Now.AddMinutes(1)), f.Now), ct);
            currentRevision = f.Challenge.TimingRevision;
        }
        finally { gate.Release.TrySetResult(); }
        await pending;
        f.Db.ChangeTracker.Clear();
        await Assert.That(await f.Db.GameplayFacts.Where(x => x.Id == id).Select(x => x.Result).SingleAsync(ct)).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(await f.Db.CompetitionChallenges.Where(x => x.Id == f.Challenge.Id).Select(x => x.TimingRevision).SingleAsync(ct)).IsEqualTo(currentRevision);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, currentRevision), f.Now, ct);
        await Assert.That(await f.Db.GameplayFacts.Where(x => x.Id == id).Select(x => x.AppliedTimingRevision).SingleAsync(ct)).IsEqualTo(currentRevision);
    });

    private sealed class BatchReadGate : DbCommandInterceptor
    {
        private int paused;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM gameplay_facts", StringComparison.Ordinal) && command.CommandText.Contains("LIMIT", StringComparison.Ordinal)
                && Interlocked.Exchange(ref paused, 1) == 0)
            { Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }

    [Test, Timeout(300_000)]
    [Arguments(false, GameplayFactTimeEligibility.Valid)] [Arguments(true, GameplayFactTimeEligibility.Valid)]
    [Arguments(false, GameplayFactTimeEligibility.NotOpened)] [Arguments(false, GameplayFactTimeEligibility.SubmissionClosed)]
    public Task A_late_checker_callback_uses_received_time_and_latest_configuration(bool receivedBeforeCutoff, GameplayFactTimeEligibility eligibility, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct, GameMode.Awdp); var at = f.Now.AddMinutes(-10);
        var team = new Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "Callback team", CaptainId = f.Competition.OwnerId,
            MemberIds = [f.Competition.OwnerId], InvitationToken = new string('g', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now };
        f.Db.Add(team); var factId = Guid.NewGuid(); var runtimeId = Guid.NewGuid();
        f.Db.Add(new AwdpTargetRuntimeInstance { Id = runtimeId, CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id,
            TeamId = team.Id, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, RunnerId = "synthetic-runner", State = RuntimeState.Running,
            GameplayFactId = factId, CreatedAt = at, RunningAt = at, ExpiresAt = f.Now.AddHours(1) });
        f.Db.Add(new FixAttemptGameplayFact { Id = factId, CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id,
            TeamId = team.Id, ActorUserId = f.Competition.OwnerId, ReferenceKind = GameplayFactReferenceKind.PatchUpload, ReferenceId = Guid.NewGuid(),
            Value = "synthetic", State = GameplayFactState.Processing, OccurredAt = at, UpdatedAt = at }); await f.Db.SaveChangesAsync(ct);
        var latestTiming = eligibility == GameplayFactTimeEligibility.NotOpened ? new ChallengeTiming(at.AddSeconds(1))
            : eligibility == GameplayFactTimeEligibility.SubmissionClosed ? new ChallengeTiming(null, null, at)
            : new ChallengeTiming(null, receivedBeforeCutoff ? at.AddSeconds(1) : at);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, latestTiming, f.Now), ct);
        var checker = new InternalResultStore(f.Db, Substitute.For<IPostCommitMessagePublisher>());
        var callback = new AwdpFixResult(factId, runtimeId, AwdpFixOutcome.DefenseSucceeded, f.Now);
        await Assert.That(await checker.RecordAwdpAsync(callback, ct)).IsEqualTo(InternalResultDisposition.Applied);
        var fact = await f.Db.GameplayFacts.SingleAsync(x => x.Id == factId, ct);
        await Assert.That(fact.Result).IsEqualTo(receivedBeforeCutoff ? GameplayFactResult.Correct : GameplayFactResult.RightButDue);
        await Assert.That(fact.TimeEligibility).IsEqualTo(eligibility);
        await Assert.That(GameplayFactCompletion.IsSuccessful(fact.Result, fact.TimeEligibility)).IsEqualTo(eligibility == GameplayFactTimeEligibility.Valid);
        await Assert.That(fact.OccurredAt).IsEqualTo(at);
        await Assert.That(fact.AppliedTimingRevision).IsEqualTo(f.Challenge.TimingRevision);
        await Assert.That(await checker.RecordAwdpAsync(callback, ct)).IsEqualTo(InternalResultDisposition.Duplicate);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(1);
    });

    [Test, Timeout(300_000)]
    public Task A_queued_blood_notification_rechecks_latest_timing_before_delivery(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct); f.Challenge.IsPublished = true;
        var team = new Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "Notification team", CaptainId = f.Competition.OwnerId,
            MemberIds = [f.Competition.OwnerId], InvitationToken = new string('f', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now };
        f.Db.Add(team); var at = f.Now.AddMinutes(-10);
        f.Db.Add(new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id,
            TeamId = team.Id, OccurredAt = at, UpdatedAt = at, Value = "synthetic", State = GameplayFactState.Completed, Result = GameplayFactResult.Correct });
        await f.Db.SaveChangesAsync(ct);
        using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
        var cache = new FusionLeaderboardCache(f.Db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>());
        var message = new BloodAwarded(f.Competition.Id, f.Challenge.Id, "Question", LeaderboardBloodRank.First, team.Id, team.Name, at);
        var delivery = new NoCTF.Infrastructure.Notifications.CompetitionNotificationDelivery(f.Db);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at), f.Now), ct);
        await NoCTF.Worker.CompetitionNotificationMessageHandlers.Handle(message, delivery, cache, TimeProvider.System, ct);
        await Assert.That(await f.Db.Notifications.CountAsync(ct)).IsEqualTo(0);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, f.Now.AddMinutes(1)), f.Now), ct);
        await NoCTF.Worker.CompetitionNotificationMessageHandlers.Handle(message, delivery, cache, TimeProvider.System, ct);
        await NoCTF.Worker.CompetitionNotificationMessageHandlers.Handle(message, delivery, cache, TimeProvider.System, ct);
        await Assert.That(await f.Db.Notifications.CountAsync(ct)).IsEqualTo(1);
    });

    [Test, Timeout(300_000)]
    public Task Preview_progression_uses_latest_other_challenge_times_before_their_batch_runs(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var others = new List<Guid>();
        for (var index = 1; index <= 2; index++)
        {
            var template = new CtfChallenge { Id = Guid.NewGuid(), OwnerId = f.Competition.OwnerId, Title = "Other " + index,
                NormalizedTitle = "OTHER " + index, Direction = "Web", Definition = TestConfigurations.Definition(GameMode.Ctf), CreatedAt = f.Now, UpdatedAt = f.Now };
            f.Db.Add(template); await f.Db.SaveChangesAsync(ct);
            var created = await f.Management.CreateAsync(new(null, f.Competition.Id, template.Id, index, f.Now), TestConfigurations.Rules(GameMode.Ctf), ct);
            others.Add(created.Challenge!.Id);
        }
        var team = new Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "Graph team", CaptainId = f.Competition.OwnerId,
            MemberIds = [f.Competition.OwnerId], InvitationToken = new string('e', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now };
        f.Db.Add(team);
        foreach (var question in new[] { f.Challenge.Id, others[0] })
            f.Db.Add(new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = question,
                TeamId = team.Id, Value = "synthetic", Result = GameplayFactResult.Correct, State = GameplayFactState.Completed,
                OccurredAt = f.Now.AddMinutes(-10), UpdatedAt = f.Now });
        var a = new ChallengeProgressionNode { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id };
        var b = new ChallengeProgressionNode { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = others[0] };
        var c = new ChallengeProgressionNode { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = others[1] };
        f.Db.Add(new CompetitionProgression { CompetitionId = f.Competition.Id, Enabled = true, Nodes = [a, b, c], Edges = [
            new() { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, SourceNodeId = a.Id, TargetNodeId = c.Id },
            new() { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, SourceNodeId = b.Id, TargetNodeId = c.Id }] });
        await f.Db.SaveChangesAsync(ct);
        await f.Store.ChangeAsync(new(f.Competition.Id, others[0], new(f.Now.AddMinutes(-5)), f.Now), ct);
        using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
        var cache = new FusionLeaderboardCache(f.Db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>());
        var preview = new ChallengeTimingPreviewReader(f.Db, cache, new PlatformSecretProtector(Options.Create(
            new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) })));
        var impact = await preview.PreviewAsync(new(f.Competition.Id, f.Challenge.Id, new(f.Now.AddMinutes(-5)), f.Now), f.Competition.OwnerId, ct);
        await Assert.That(impact!.Teams.Single().ProgressionNodesChanged).IsEqualTo(1);
    });

    [Test, Timeout(300_000)]
    public Task Interrupted_batches_and_late_jobs_converge_to_the_latest_saved_revision(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct); var at = f.Now.AddMinutes(-10);
        f.Db.AddRange(Enumerable.Range(0, 205).Select(_ => new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, Value = "synthetic", State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct, OccurredAt = at, UpdatedAt = at })); await f.Db.SaveChangesAsync(ct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddSeconds(-1)), f.Now), ct);
        var oldRevision = f.Challenge.TimingRevision;
        await using (var transaction = await f.Db.Database.BeginTransactionAsync(ct))
        { await f.Store.RecalculateAsync(new(f.Challenge.Id, oldRevision), f.Now, ct); await transaction.RollbackAsync(ct); }
        f.Db.ChangeTracker.Clear();
        await Assert.That(await f.Db.GameplayFacts.CountAsync(x => x.Result == GameplayFactResult.RightButDue, ct)).IsEqualTo(0);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, oldRevision), f.Now, ct);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(x => x.Result == GameplayFactResult.RightButDue, ct)).IsEqualTo(200);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddSeconds(1)), f.Now), ct);
        var latestRevision = await f.Db.CompetitionChallenges.Where(x => x.Id == f.Challenge.Id).Select(x => x.TimingRevision).SingleAsync(ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, oldRevision), f.Now, ct);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(x => x.Result == GameplayFactResult.RightButDue, ct)).IsEqualTo(200);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, latestRevision), f.Now, ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, latestRevision), f.Now, ct);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(x => x.Result == GameplayFactResult.Correct && x.AppliedTimingRevision == latestRevision, ct)).IsEqualTo(205);
        await Assert.That(await f.Db.CompetitionChallenges.Where(x => x.Id == f.Challenge.Id).Select(x => x.AppliedTimingRevision).SingleAsync(ct)).IsEqualTo(latestRevision);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(205);
    });

    [Test, Timeout(300_000)]
    [Arguments(GameMode.Ctf)] [Arguments(GameMode.Awd)] [Arguments(GameMode.Awdp)] [Arguments(GameMode.Koh)]
    public Task PostgreSql_latest_cutoff_preserves_manual_points_and_restores_correctness_for_each_mode(GameMode mode, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct, mode);
        var team = new Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "Timing team", CaptainId = f.Competition.OwnerId,
            MemberIds = [f.Competition.OwnerId], InvitationToken = new string('b', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now };
        f.Db.Add(team); var at = f.Now.AddMinutes(-10);
        GameplayFact correct = mode == GameMode.Awdp ? new BreakAttemptGameplayFact() : mode == GameMode.Koh ? new KohControlObservationGameplayFact() : new FlagAttemptGameplayFact();
        correct.Id = Guid.NewGuid(); correct.CompetitionId = f.Competition.Id; correct.CompetitionChallengeId = f.Challenge.Id;
        correct.TeamId = team.Id; correct.OccurredAt = at; correct.UpdatedAt = at; correct.State = GameplayFactState.Completed;
        correct.Result = mode == GameMode.Koh ? GameplayFactResult.Controlled : GameplayFactResult.Correct; correct.Value = "synthetic";
        Guid? victimId = null;
        if (mode == GameMode.Awd)
        {
            var victimUser = Guid.NewGuid(); victimId = Guid.NewGuid();
            f.Db.Add(new User { Id = victimUser, UserName = "victim", NormalizedUserName = "VICTIM", Email = "victim@test.invalid", NormalizedEmail = "VICTIM@TEST.INVALID", PasswordHash = "synthetic", CreatedAt = f.Now, UpdatedAt = f.Now });
            f.Db.Add(new Team { Id = victimId.Value, CompetitionId = f.Competition.Id, Name = "Victim", NormalizedName = "VICTIM", CaptainId = victimUser,
                MemberIds = [victimUser], InvitationToken = new string('d', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now });
            correct.VictimTeamId = victimId; correct.ReferenceKind = GameplayFactReferenceKind.AwdRound; correct.ReferenceId = Guid.NewGuid();
        }
        f.Db.Add(correct); f.Db.Add(new ManualAdjustmentGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, TeamId = team.Id, OccurredAt = at, UpdatedAt = at,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Applied, Value = "25" }); await f.Db.SaveChangesAsync(ct);
        using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
        var cache = new FusionLeaderboardCache(f.Db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>());
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddSeconds(-1)), f.Now), ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        var stopped = await cache.CreateScoreboardAsync(f.Competition.Id, f.Now, ct);
        await Assert.That(stopped!.Snapshot.Teams.Single(x => x.TeamId == team.Id).TotalScore).IsEqualTo(25);
        if (victimId is { } victim) await Assert.That(stopped.Snapshot.Teams.Single(x => x.TeamId == victim).TotalScore).IsEqualTo(0);
        await Assert.That(correct.Result).IsEqualTo(mode == GameMode.Koh ? GameplayFactResult.Controlled : GameplayFactResult.RightButDue);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, f.Now.AddMinutes(1)), f.Now), ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        var restored = await cache.CreateScoreboardAsync(f.Competition.Id, f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(mode == GameMode.Koh ? GameplayFactResult.Controlled : GameplayFactResult.Correct);
        await Assert.That(restored!.Snapshot.Teams.Single(x => x.TeamId == team.Id).TotalScore).IsGreaterThan(25);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(2);
    });

    [Test, Timeout(300_000)]
    public Task Http_timing_preview_requires_management_and_patch_preserves_omitted_and_clears_null(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var access = Substitute.For<ICompetitionModerationAuthorizer>();
        access.CanModerateAsync(f.Competition.OwnerId, f.Competition.Id, Arg.Any<CancellationToken>()).Returns(true);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options => {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(PreviewChallengeTimingEndpoint).Assembly];
            options.Filter = type => type == typeof(PreviewChallengeTimingEndpoint) || type == typeof(PatchCompetitionChallengeEndpoint) || type == typeof(SubmitFlagEndpoint);
        });
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestBearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(f.Db);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IUserContext>(new TestUserContext(f.Competition.OwnerId));
        builder.Services.AddSingleton(access);
        var messages = Substitute.For<IPostCommitMessagePublisher>();
        builder.Services.AddSingleton<IChallengeManagementStore>(f.Management);
        builder.Services.AddSingleton(new GetChallenge(f.Management));
        builder.Services.AddSingleton(new UpdateChallenge(f.Management));
        var configuration = new ChallengeConfigurationStore(f.Db, messages, NullCompetitionEventRecorder.Instance);
        builder.Services.AddSingleton(new GetChallengeConfiguration(configuration));
        builder.Services.AddSingleton(new UpdateChallengeConfiguration(configuration, new GameModeChallengeConfigurationCatalog()));
        builder.Services.AddSingleton<IAtomicAggregatePatch>(new AggregatePatchTransaction(f.Db, messages));
        builder.Services.AddFusionCache(NoCtfCacheNames.Leaderboards);
        builder.Services.AddSingleton<ILeaderboardSnapshotFactory>(sp => new FusionLeaderboardCache(f.Db,
            new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()), Substitute.For<ILeaderboardRefreshPublisher>(), sp.GetRequiredService<IFusionCacheProvider>()));
        builder.Services.AddSingleton(new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) })));
        builder.Services.AddSingleton<IChallengeTimingPreviewReader, ChallengeTimingPreviewReader>();
        builder.Services.AddSingleton<IChallengeTimingStore>(f.Store);
        builder.Services.AddSingleton<ManageChallengeTiming>();
        builder.Services.AddSingleton(new SubmitFlag(new GameplayFactIntakeStore(f.Db, messages, new GameplayFactAttemptCriticalSection()), new GameModeGameplayFactAdmissionPolicy()));
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints(); await app.StartAsync(ct);
        using var client = app.GetTestClient();
        var uri = $"/api/v1/admin/competitions/{f.Competition.Id}/challenges/{f.Challenge.Id}";
        using var anonymous = await client.PostAsJsonAsync(uri + "/timing-preview", new { timing = new { } }, ct);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic");
        access.CanModerateAsync(f.Competition.OwnerId, f.Competition.Id, Arg.Any<CancellationToken>()).Returns(false);
        using var forbidden = await client.PostAsJsonAsync(uri + "/timing-preview", new { timing = new { } }, ct);
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        access.CanModerateAsync(f.Competition.OwnerId, f.Competition.Id, Arg.Any<CancellationToken>()).Returns(true);
        using var invalid = await client.PostAsJsonAsync(uri + "/timing-preview", new { timing = new { autoOpenAt = f.Now.AddHours(2), scoringEndsAt = f.Now } }, ct);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, f.Now.AddMinutes(20), f.Now.AddMinutes(30)), f.Now), ct);
        using var preserve = await client.PatchAsJsonAsync(uri, new { timing = new { scoringEndsAt = f.Now.AddMinutes(10) } }, ct);
        await Assert.That(preserve.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.SubmissionDeadlineAt).IsEqualTo(f.Now.AddMinutes(30));
        f.Db.Add(new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id,
            Value = "synthetic", Result = GameplayFactResult.Correct, State = GameplayFactState.Completed, OccurredAt = f.Now.AddMinutes(-2), UpdatedAt = f.Now });
        await f.Db.SaveChangesAsync(ct);
        using var unconfirmed = await client.PatchAsJsonAsync(uri, new { timing = new { scoringEndsAt = (DateTimeOffset?)null } }, ct);
        await Assert.That(unconfirmed.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var preview = await client.PostAsJsonAsync(uri + "/timing-preview", new { timing = new { scoringEndsAt = (DateTimeOffset?)null } }, ct);
        await Assert.That(preview.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await preview.Content.ReadAsStringAsync(ct));
        var token = json.RootElement.GetProperty("token").GetString();
        using var clear = await client.PatchAsJsonAsync(uri, new { timing = new { scoringEndsAt = (DateTimeOffset?)null }, timingPreviewToken = token }, ct);
        await Assert.That(clear.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var refreshed = await f.Db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes().SingleAsync(x => x.Id == f.Challenge.Id, ct);
        await Assert.That(refreshed.ScoringEndsAt).IsNull();
        await Assert.That(refreshed.SubmissionDeadlineAt).IsEqualTo(f.Now.AddMinutes(30));
        using var mixed = await client.PatchAsJsonAsync(uri, new { timing = new { }, presentation = new { order = 0, isPublished = false } }, ct);
        await Assert.That(mixed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        f.Db.ChangeTracker.Clear();
        var question = await f.Db.CompetitionChallenges.SingleAsync(x => x.Id == f.Challenge.Id, ct);
        question.IsPublished = true; question.ScoringEndsAt = f.Now.AddMinutes(-5); question.SubmissionDeadlineAt = f.Now.AddMinutes(-1);
        f.Db.Add(new Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "HTTP team", CaptainId = f.Competition.OwnerId,
            MemberIds = [f.Competition.OwnerId], InvitationToken = new string('c', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now });
        await f.Db.SaveChangesAsync(ct);
        var count = await f.Db.GameplayFacts.CountAsync(ct);
        var submissionUri = $"/api/v1/competitions/{f.Competition.Id}/challenges/{f.Challenge.Id}/flag-submissions";
        using var closed = await client.PostAsJsonAsync(submissionUri, new { flag = "synthetic" }, ct);
        await Assert.That(closed.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var closedJson = JsonDocument.Parse(await closed.Content.ReadAsStringAsync(ct));
        await Assert.That(closedJson.RootElement.GetProperty("code").GetString()).IsEqualTo("ChallengeSubmissionClosed");
        question.AutoOpenAt = f.Now.AddMinutes(1); question.ScoringEndsAt = f.Now.AddMinutes(2); question.SubmissionDeadlineAt = f.Now.AddMinutes(3);
        await f.Db.SaveChangesAsync(ct);
        using var notOpen = await client.PostAsJsonAsync(submissionUri, new { flag = "synthetic" }, ct);
        await Assert.That(notOpen.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var notOpenJson = JsonDocument.Parse(await notOpen.Content.ReadAsStringAsync(ct));
        await Assert.That(notOpenJson.RootElement.GetProperty("code").GetString()).IsEqualTo("ChallengeNotOpened");
        await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(count);

    });

    private sealed class TestUserContext(Guid userId) : IUserContext { public Guid UserId => userId; public bool IsAdministrator => true; }
    private sealed class TestBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization.Count == 0
            ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "synthetic")], Scheme.Name)), Scheme.Name)));
    }

    [Test, Timeout(300_000)]
    public Task Preview_uses_candidate_rules_without_writes_and_confirmation_rejects_changed_data(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var team = new Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "Preview team", CaptainId = f.Competition.OwnerId,
            MemberIds = [f.Competition.OwnerId], InvitationToken = new string('a', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now };
        f.Db.Add(team);
        var fact = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id,
            TeamId = team.Id, ActorUserId = f.Competition.OwnerId, OccurredAt = f.Now.AddMinutes(-10), UpdatedAt = f.Now,
            Result = GameplayFactResult.Correct, State = GameplayFactState.Completed, Value = "synthetic" };
        f.Db.Add(fact); await f.Db.SaveChangesAsync(ct);
        using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
        var cache = new FusionLeaderboardCache(f.Db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>());
        var protector = new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
        var previewReader = new ChallengeTimingPreviewReader(f.Db, cache, protector);
        var candidate = new ChangeChallengeTiming(f.Competition.Id, f.Challenge.Id, new(null, f.Now.AddMinutes(-20)), f.Now);
        var preview = await previewReader.PreviewAsync(candidate, f.Competition.OwnerId, ct);
        await Assert.That(preview).IsNotNull();
        await Assert.That(preview!.AffectedAttempts).IsEqualTo(1);
        await Assert.That(preview.Teams.Single().ScoreBefore).IsGreaterThan(0);
        await Assert.That(preview.Teams.Single().ScoreAfter).IsEqualTo(0);
        await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.ScoringEndsAt).IsNull();
        await Assert.That(await previewReader.ValidateAsync(candidate, f.Competition.OwnerId, preview.Token, ct)).IsTrue();
        await Assert.That(await previewReader.ValidateAsync(candidate, Guid.NewGuid(), preview.Token, ct)).IsFalse();
        fact.UpdatedAt = f.Now.AddSeconds(1); await f.Db.SaveChangesAsync(ct);
        await Assert.That(await previewReader.ValidateAsync(candidate, f.Competition.OwnerId, preview.Token, ct)).IsFalse();
        await cache.RefreshAsync(f.Competition.Id, ct);
        await Assert.That((await cache.GetScoreboardAsync(f.Competition.Id, ct))!.Snapshot.Teams.Single().TotalScore).IsGreaterThan(0);
        await f.Store.ChangeAsync(candidate, ct);
        // No invalidation event is delivered in this fixture. A cached old-policy
        // projection must still be rejected before the background batch runs.
        await Assert.That((await cache.GetScoreboardAsync(f.Competition.Id, ct))!.Snapshot.Teams.Single().TotalScore).IsEqualTo(0);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        var restore = candidate with { Timing = new(), Now = f.Now.AddSeconds(2) };
        var restorePreview = await previewReader.PreviewAsync(restore, f.Competition.OwnerId, ct);
        await Assert.That(restorePreview!.Teams.Single().ScoreBefore).IsEqualTo(0);
        await Assert.That(restorePreview.Teams.Single().ScoreAfter).IsGreaterThan(0);
        await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.RightButDue);
    });
    [Test, Timeout(300_000)]
    public Task Opening_waits_for_resume_and_a_canceled_or_stale_plan_cannot_publish(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var timing = new ChallengeTiming(f.Now, f.Now.AddMinutes(30), f.Now.AddHours(1));
        await Assert.That(await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct)).IsNull();
        var revision = f.Challenge.TimingRevision;
        f.Competition.Status = CompetitionStatus.Paused; await f.Db.SaveChangesAsync(ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now, ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        f.Competition.Status = CompetitionStatus.Running; await f.Db.SaveChangesAsync(ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now, ct);
        await Assert.That(f.Challenge.IsPublished).IsTrue();
        await f.Management.UpdateAsync(new(f.Competition.Id, f.Challenge.Id, 0, false, f.Now), ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now.AddSeconds(5), ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        await Assert.That(f.Challenge.AutoOpenAt).IsEqualTo(f.Now);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing with { AutoOpenAt = f.Now.AddMinutes(5) }, f.Now), ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now.AddMinutes(10), ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        var schedules = await new ChallengeTimingScheduleSource(f.Db).RebuildAsync(f.Now, ct);
        await Assert.That(schedules.Any(x => x.Message is AdvanceChallengeOpening opening && opening.TimingRevision == f.Challenge.TimingRevision)).IsTrue();
    });

    [Test, Timeout(300_000)]
    public Task Latest_time_rules_reclassify_existing_correct_and_wrong_facts_without_new_attempts_or_checker_execution(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var at = f.Now.AddMinutes(-10);
        var correct = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, OccurredAt = at, UpdatedAt = at,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Correct, Value = "synthetic" };
        var wrong = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, OccurredAt = at, UpdatedAt = at,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Wrong, Value = "wrong" };
        f.Db.AddRange(correct, wrong); await f.Db.SaveChangesAsync(ct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddMinutes(-1)), f.Now), ct);
        var stale = f.Challenge.TimingRevision;
        await f.Store.RecalculateAsync(new(f.Challenge.Id, stale), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await Assert.That(wrong.Result).IsEqualTo(GameplayFactResult.Wrong);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddMinutes(1)), f.Now), ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, stale), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.Correct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(at.AddMinutes(1), at.AddMinutes(2)), f.Now), ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        await Assert.That(correct.TimeEligibility).IsEqualTo(GameplayFactTimeEligibility.NotOpened);
        await Assert.That(wrong.TimeEligibility).IsEqualTo(GameplayFactTimeEligibility.NotOpened);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(2);
        await Assert.That(await f.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
    });

    [Test, Timeout(300_000)]
    public Task Timing_updates_rollback_and_other_presentation_updates_preserve_the_schedule(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var timing = new ChallengeTiming(f.Now.AddMinutes(5), f.Now.AddMinutes(30), f.Now.AddHours(1));
        await using (var transaction = await f.Db.Database.BeginTransactionAsync(ct))
        { await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct); await transaction.RollbackAsync(ct); }
        f.Db.ChangeTracker.Clear(); await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.AutoOpenAt).IsNull();
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct);
        await f.Management.UpdateAsync(new(f.Competition.Id, f.Challenge.Id, 0, false, f.Now, "renamed"), ct);
        await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.AutoOpenAt).IsEqualTo(timing.AutoOpenAt);
        await Assert.That(f.Challenge.OpeningState).IsEqualTo(ChallengeOpeningState.Pending);
    });

    private sealed class Fixture(PostgreSqlContainer postgres, NoCtfDbContext db, Competition competition,
        CompetitionChallenge challenge, DateTimeOffset now) : IAsyncDisposable
    {
        public NoCtfDbContext Db { get; } = db;
        public Competition Competition { get; } = competition;
        public CompetitionChallenge Challenge { get; } = challenge;
        public DateTimeOffset Now { get; } = now;
        public ChallengeManagementStore Management { get; } = new(db, Substitute.For<IPostCommitMessagePublisher>(), Substitute.For<IChallengeRuntimeTemplateCatalog>());
        public ChallengeTimingStore Store => new(Db, Substitute.For<IPostCommitMessagePublisher>(), Management, NullCompetitionEventRecorder.Instance);
        public static async Task<Fixture> CreateAsync(CancellationToken ct, GameMode mode = GameMode.Ctf)
        {
            var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(),
                setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
            var db = new NoCtfDbContext(options); await db.Database.MigrateAsync(ct);
            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); var owner = Guid.NewGuid();
            db.Users.Add(new User { Id = owner, UserName = "timing-owner", Email = "timing@test.invalid", PasswordHash = "test",
                AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now });
            var competition = CompetitionGeneratedCatalog.Create(mode);
            competition.Id = Guid.NewGuid(); competition.OwnerId = owner; competition.Title = "Timing";
            competition.Status = CompetitionStatus.Running; competition.StartAt = now.AddHours(-1); competition.EndAt = now.AddHours(1);
            competition.ModeConfiguration = TestConfigurations.Competition(mode); competition.FlagDerivationSecret = new byte[32]; competition.CreatedAt = now; competition.UpdatedAt = now;
            db.Add(competition);
            var template = ChallengeGeneratedCatalog.Create(mode); template.Id = Guid.NewGuid(); template.OwnerId = owner;
            template.Title = "Timing template"; template.Direction = "Web"; template.Definition = TestConfigurations.Definition(mode); template.CreatedAt = now; template.UpdatedAt = now;
            db.Add(template); await db.SaveChangesAsync(ct);
            var management = new ChallengeManagementStore(db, Substitute.For<IPostCommitMessagePublisher>(), Substitute.For<IChallengeRuntimeTemplateCatalog>());
            var created = await management.CreateAsync(new(null, competition.Id, template.Id, 0, now), TestConfigurations.Rules(mode), ct);
            var challenge = await db.CompetitionChallenges.SingleAsync(x => x.Id == created.Challenge!.Id, ct);
            return new(postgres, db, competition, challenge, now);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await postgres.DisposeAsync(); }
    }
}
