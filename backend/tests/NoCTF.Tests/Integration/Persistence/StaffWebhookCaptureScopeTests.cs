using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.StaffWebhooks;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Fixtures.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration"), Category("StaffWebhooks")]
public sealed partial class StaffWebhookCaptureScopeTests
{
    [Test, Timeout(300_000)]
    public Task Scoped_capture_matches_full_capture_and_commits_source_events_and_summaries_together(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 6, 8, 4, ct);
            foreach (var change in Enum.GetValues<CaptureChange>())
            {
                var full = await CaptureAndRollbackAsync(options, fixture, change, true, ct);
                var scoped = await CaptureAndRollbackAsync(options, fixture, change, false, ct);
                await Assert.That(scoped).IsEqualTo(full);
            }
            await using var verification = new NoCtfDbContext(options);
            await Assert.That(await verification.StaffWebhookEvents.CountAsync(ct)).IsEqualTo(0);
            await Assert.That((await verification.StaffWebhookStreams.SingleAsync(ct)).Sequence).IsEqualTo(fixture.InitialSequence);
        });

    [Test, Timeout(300_000)]
    public Task Scoped_history_reads_are_bounded_by_affected_items_and_preserve_unrelated_pending_items(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 32, 12, 16, ct);
            var reads = new ProjectionReadMetrics();
            var counted = new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(reads).Options;
            var full = await CaptureOnlyAsync(counted, fixture, true, reads, ct);
            var scoped = await CaptureOnlyAsync(counted, fixture, false, reads, ct);
            await Assert.That(scoped.CanonicalEvents).IsEqualTo(full.CanonicalEvents);
            await Assert.That(scoped.EventCount).IsEqualTo(1);
            await Assert.That(scoped.ReturnedRows).IsLessThan(full.ReturnedRows / 4);
            await Assert.That(scoped.ReturnedRows).IsLessThan(32L);
            await Assert.That(scoped.Commands.Any(command => command.Columns > 1 && (command.Sql.Contains("gameplay_facts", StringComparison.OrdinalIgnoreCase)
                || command.Sql.Contains("competition_events", StringComparison.OrdinalIgnoreCase)))).IsFalse();
            await Assert.That(scoped.Commands.Any(command => command.Sql.Contains("notifications", StringComparison.OrdinalIgnoreCase)
                && command.Sql.Contains("thread_root_id", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(scoped.Commands.Any(command => command.Sql.Contains("n.body", StringComparison.OrdinalIgnoreCase)
                || command.Sql.Contains("secret_ciphertext", StringComparison.OrdinalIgnoreCase))).IsFalse();
        });

    [Test, Timeout(300_000)]
    public Task The_first_capture_rebuilds_preexisting_items_instead_of_only_the_latest_thread(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 4, 3, 2, ct, establishBaseline: false);
            await using var db = new NoCtfDbContext(options, new FixedClock(fixture.Now));
            db.Notifications.Add(Reply(fixture, Id(900_001), fixture.QuestionIds[0], CompetitionQuestionParticipantRole.Participant, fixture.Now));
            await db.SaveChangesAsync(ct);
            await Assert.That(await db.StaffWebhookWorkItems.CountAsync(ct)).IsEqualTo(7);
            await Assert.That(await db.StaffWebhookEvents.CountAsync(ct)).IsEqualTo(7);
            await Assert.That(await db.StaffWebhookEvents.AllAsync(value => value.Kind == StaffWebhookEventKind.WorkItemCreated, ct)).IsTrue();
        });

    [Test, Timeout(300_000)]
    public Task Platform_consultations_and_context_free_reply_nodes_commit_nullable_challenge_enrichment(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 2, 2, 2, ct);
            await using var db = new NoCtfDbContext(options, new FixedClock(fixture.Now));
            var root = Question(fixture, Id(900_001), fixture.Now.AddSeconds(-1), CompetitionQuestionParticipantRole.Judge);
            root.QuestionSubject = CompetitionQuestionSubject.Platform; root.CompetitionChallengeId = null;
            db.Notifications.Add(root);
            db.Notifications.Add(Reply(fixture, Id(900_002), root.Id, CompetitionQuestionParticipantRole.Participant, fixture.Now));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            var summary = (await db.StaffWebhookWorkItems.SingleAsync(value => value.ItemId == root.Id, ct)).Summary;
            await Assert.That(summary.ChallengeId).IsNull();
            await Assert.That(summary.ChallengeTitle).IsNull();
            await Assert.That(summary.Direction).IsNull();
            await Assert.That(summary.Subject).IsEqualTo(CompetitionQuestionSubject.Platform);
            await Assert.That(summary.RequiresStaffAction).IsTrue();
            await Assert.That(summary.ActionRequiredSince).IsEqualTo(fixture.Now);
            var full = await StaffWorkItemProjection.ReadAsync(db, fixture.CompetitionId, ct);
            await Assert.That(full.Single(value => value.Kind == StaffWorkItemKind.Consultation && value.Id == root.Id).ChallengeId).IsNull();
        });

    [Test, Timeout(300_000)]
    public Task Metadata_committed_in_another_context_retains_full_capture_catch_up(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 6, 4, 4, ct);
            foreach (var change in Enum.GetValues<MetadataChange>())
            {
                await using (var writer = new NoCtfDbContext(options))
                {
                    switch (change)
                    {
                        case MetadataChange.Title:
                            (await writer.Challenges.SingleAsync(ct)).Title = "Separately renamed challenge";
                            break;
                        case MetadataChange.Direction:
                            (await writer.Challenges.SingleAsync(ct)).Direction = "Crypto";
                            break;
                        case MetadataChange.Team:
                            (await writer.Teams.SingleAsync(value => value.Id == fixture.TeamId, ct)).Name = "Separately renamed Blue";
                            break;
                        case MetadataChange.RelatedTeam:
                            var related = await writer.Teams.SingleAsync(value => value.Id == fixture.RelatedTeamId, ct);
                            related.Name = "Separately renamed Red"; related.DeletedAt = fixture.Now;
                            break;
                        case MetadataChange.MissingReference:
                            // A stored enrichment whose reference is absent must also trigger catch-up.
                            var row = await writer.StaffWebhookWorkItems.SingleAsync(value => value.Kind == StaffWorkItemKind.BanAppeal, ct);
                            row.Summary.ChallengeId = Id(999_999); row.Summary.ChallengeTitle = "Obsolete reference"; row.Summary.Direction = "Unknown";
                            break;
                    }
                    await writer.SaveChangesAsync(ct);
                }
                var full = await CaptureAndRollbackAsync(options, fixture, CaptureChange.ParticipantReply, true, ct);
                var actual = await CaptureAndRollbackAsync(options, fixture, CaptureChange.ParticipantReply, false, ct);
                await Assert.That(actual).IsEqualTo(full);
                // Commit the caught-up baseline so each case independently exercises one stale field.
                await using var baseline = new NoCtfDbContext(options, new FixedClock(fixture.Now));
                await StaffWebhookEventCapture.CaptureAsync(baseline, [fixture.CompetitionId], fixture.Now, ct, force: true);
                await baseline.SaveChangesAsync(ct);
                fixture = fixture with { InitialSequence = (await baseline.StaffWebhookStreams.SingleAsync(ct)).Sequence };
                await baseline.StaffWebhookEvents.ExecuteDeleteAsync(ct);
            }
        });

    [Test, Timeout(300_000)]
    public Task Bulk_incident_updates_and_removal_catch_up_once_without_permanent_full_rebuilds(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 8, 4, 4, ct);
            var reads = new ProjectionReadMetrics();
            var counted = new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(reads).Options;
            foreach (var change in Enum.GetValues<BulkIncidentChange>())
            {
                await using (var writer = new NoCtfDbContext(options))
                {
                    var id = change == BulkIncidentChange.Deleted ? fixture.IncidentIds[2]
                        : change == BulkIncidentChange.ResolvedUpdatedAt ? fixture.IncidentIds[1] : fixture.IncidentIds[0];
                    var query = writer.GameplayFacts.Where(value => value.Id == id);
                    var affected = change switch
                    {
                        BulkIncidentChange.UpdatedAt or BulkIncidentChange.ResolvedUpdatedAt => await query
                            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.UpdatedAt, fixture.Now), ct),
                        BulkIncidentChange.Reason => await query.ExecuteUpdateAsync(setters => setters
                            .SetProperty(value => value.FailureCode, GameplayFactFailureCode.ForeignTeamFlagDetected), ct),
                        BulkIncidentChange.Cleared => await query.ExecuteUpdateAsync(setters => setters
                            .SetProperty(value => value.FailureCode, (GameplayFactFailureCode?)null), ct),
                        BulkIncidentChange.Deleted => await query.ExecuteDeleteAsync(ct),
                        BulkIncidentChange.MetadataAfterClosure => await writer.Challenges.Where(value => value.Id == fixture.TemplateId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Title, "Renamed after incident closure"), ct),
                        _ => throw new ArgumentOutOfRangeException(nameof(change))
                    };
                    await Assert.That(affected).IsEqualTo(1);
                }
                var full = await CaptureAndRollbackAsync(options, fixture, CaptureChange.ParticipantReply, true, ct);
                var actual = await CaptureAndRollbackAsync(options, fixture, CaptureChange.ParticipantReply, false, ct);
                await Assert.That(actual).IsEqualTo(full);
                await using (var baseline = new NoCtfDbContext(options, new FixedClock(fixture.Now)))
                {
                    await StaffWebhookEventCapture.CaptureAsync(baseline, [fixture.CompetitionId], fixture.Now, ct, force: true);
                    await baseline.SaveChangesAsync(ct);
                    fixture = fixture with { InitialSequence = (await baseline.StaffWebhookStreams.SingleAsync(ct)).Sequence };
                    await baseline.StaffWebhookEvents.ExecuteDeleteAsync(ct);
                }
                var clean = await CaptureOnlyAsync(counted, fixture, false, reads, ct);
                await Assert.That(clean.EventCount).IsEqualTo(1);
                await Assert.That(clean.ReturnedRows).IsLessThan(24L);
                await Assert.That(clean.Commands.Any(command => command.Columns > 1
                    && command.Sql.Contains("gameplay_facts", StringComparison.OrdinalIgnoreCase))).IsFalse();
            }
        });

    private static async Task<string> CaptureAndRollbackAsync(DbContextOptions<NoCtfDbContext> options,
        CaptureFixture fixture, CaptureChange change, bool full, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options, new FixedClock(fixture.Now));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await ApplyAsync(db, fixture, change, ct);
        if (full) await StaffWebhookEventCapture.CaptureAsync(db, [fixture.CompetitionId], fixture.Now, ct, force: true);
        await db.SaveChangesAsync(ct);
        var events = await db.StaffWebhookEvents.AsNoTracking().OrderBy(value => value.Sequence).ToArrayAsync(ct);
        var summaries = await db.StaffWebhookWorkItems.AsNoTracking().OrderBy(value => value.Kind).ThenBy(value => value.ItemId).ToArrayAsync(ct);
        var stream = await db.StaffWebhookStreams.AsNoTracking().SingleAsync(ct);
        var result = JsonSerializer.Serialize(new { Change = change, Events = CanonicalEvents(events),
            Items = summaries.Select(value => value.Summary), stream.Sequence, stream.LatestBusinessSequence });
        await Assert.That(result.Contains("PROTECTED_", StringComparison.Ordinal)).IsFalse();
        await Assert.That(events.All(value => value.Sequence > fixture.InitialSequence)).IsTrue();
        await transaction.RollbackAsync(ct);
        return result;
    }

    private static async Task<CaptureObservation> CaptureOnlyAsync(DbContextOptions<NoCtfDbContext> options,
        CaptureFixture fixture, bool full, ProjectionReadMetrics? reads, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options, new FixedClock(fixture.Now));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        db.Notifications.Add(Reply(fixture, Id(900_001), fixture.QuestionIds[0], CompetitionQuestionParticipantRole.Participant, fixture.Now));
        reads?.Reset();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        // force selects the full reference projection but bypasses its subscription probe.
        // Keep that same authoritative probe in both measured variants.
        if (full && !await db.StaffWebhookTargets.AnyAsync(value => value.CompetitionId == fixture.CompetitionId && value.Enabled && !value.Deleted, ct))
            throw new InvalidOperationException("The measured subscription must be enabled.");
        await StaffWebhookEventCapture.CaptureAsync(db, [fixture.CompetitionId], fixture.Now, ct, force: full);
        var milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var collections = Enumerable.Range(0, 3).Select(index => GC.CollectionCount(index) - gcBefore[index]).ToArray();
        var events = db.ChangeTracker.Entries<StaffWebhookEvent>().Where(value => value.State == EntityState.Added).Select(value => value.Entity).ToArray();
        var commands = reads?.Commands.ToArray() ?? [];
        var observation = new CaptureObservation(CanonicalEvents(events), events.Length, milliseconds, allocated,
            commands.Sum(value => value.Rows), commands.Sum(value => value.Rows * value.Columns), collections, commands);
        await transaction.RollbackAsync(ct);
        return observation;
    }

    private static string CanonicalEvents(IEnumerable<StaffWebhookEvent> events) => JsonSerializer.Serialize(events.OrderBy(value => value.Sequence)
        .Select(value => new { value.CompetitionId, value.CompetitionTitle, value.Sequence, value.LatestBusinessSequence,
            value.Kind, value.ChangeKind, value.OccurredAt, Items = value.Items.OrderBy(item => item.Position).Select(item => item.Summary) }));

    private static async Task ApplyAsync(NoCtfDbContext db, CaptureFixture fixture, CaptureChange change, CancellationToken ct)
    {
        switch (change)
        {
            case CaptureChange.ParticipantReply:
            case CaptureChange.StaffReply:
                db.Notifications.Add(Reply(fixture, Id(900_001), fixture.QuestionIds[0], change == CaptureChange.ParticipantReply
                    ? CompetitionQuestionParticipantRole.Participant : CompetitionQuestionParticipantRole.Judge, fixture.Now));
                break;
            case CaptureChange.QuestionClosed:
                db.Notifications.Add(new QuestionStatusChangedNotification { Id = Id(900_001), ThreadRootId = fixture.QuestionIds[0],
                    TargetType = NotificationTargetType.CompetitionCollaborators, TargetId = fixture.CompetitionId, SourceId = fixture.OwnerId,
                    QuestionStatus = CompetitionQuestionStatus.Closed, QuestionActorRole = CompetitionQuestionParticipantRole.Judge, SentAt = fixture.Now });
                break;
            case CaptureChange.NewThreadWithPendingReply:
                db.Notifications.Add(Question(fixture, Id(900_001), fixture.Now.AddSeconds(-1), CompetitionQuestionParticipantRole.Judge));
                db.Notifications.Add(Reply(fixture, Id(900_002), Id(900_001), CompetitionQuestionParticipantRole.Participant, fixture.Now));
                break;
            case CaptureChange.IncidentDismissed:
                db.CompetitionEvents.Add(new CheatIncidentDismissedEvent { Id = Id(900_001), CompetitionId = fixture.CompetitionId,
                    GameplayFactId = fixture.IncidentIds[0], ActorUserId = fixture.OwnerId, OccurredAt = fixture.Now });
                break;
            case CaptureChange.IncidentReasonChanged:
                var incident = await db.GameplayFacts.SingleAsync(value => value.Id == fixture.IncidentIds[0], ct);
                incident.FailureCode = GameplayFactFailureCode.ForeignTeamFlagDetected; incident.UpdatedAt = fixture.Now;
                break;
            case CaptureChange.IncidentCleared:
                var cleared = await db.GameplayFacts.SingleAsync(value => value.Id == fixture.IncidentIds[0], ct);
                cleared.FailureCode = null; cleared.UpdatedAt = fixture.Now;
                break;
            case CaptureChange.IncidentRemovedWithReply:
                db.GameplayFacts.Remove(await db.GameplayFacts.SingleAsync(value => value.Id == fixture.IncidentIds[0], ct));
                db.Notifications.Add(Reply(fixture, Id(900_001), fixture.QuestionIds[0], CompetitionQuestionParticipantRole.Participant, fixture.Now));
                break;
            case CaptureChange.NewIncidentAndReply:
                db.GameplayFacts.Add(Incident(fixture, Id(900_001), fixture.Now));
                db.Notifications.Add(Reply(fixture, Id(900_002), fixture.QuestionIds[0], CompetitionQuestionParticipantRole.Participant, fixture.Now));
                break;
            case CaptureChange.TeamMetadataWithReply:
                (await db.Teams.SingleAsync(value => value.Id == fixture.TeamId, ct)).Name = "Renamed team";
                db.Notifications.Add(Reply(fixture, Id(900_001), fixture.QuestionIds[0], CompetitionQuestionParticipantRole.Participant, fixture.Now));
                break;
            case CaptureChange.TemplateMetadataWithReply:
                var template = await db.Challenges.SingleAsync(value => value.Id == fixture.TemplateId, ct);
                template.Title = "Renamed challenge"; template.Direction = "Misc";
                db.Notifications.Add(Reply(fixture, Id(900_001), fixture.QuestionIds[0], CompetitionQuestionParticipantRole.Participant, fixture.Now));
                break;
            case CaptureChange.TeamUnbanned:
                (await db.Teams.SingleAsync(value => value.Id == fixture.TeamId, ct)).IsBanned = false;
                db.CompetitionEvents.Add(new TeamUnbannedEvent { Id = Id(900_001), CompetitionId = fixture.CompetitionId,
                    TeamId = fixture.TeamId, OccurredAt = fixture.Now });
                break;
            case CaptureChange.AppealAccepted:
                db.CompetitionEvents.Add(new TeamBanAppealAcceptedEvent { Id = Id(900_001), CompetitionId = fixture.CompetitionId,
                    ParentEventId = fixture.AppealId, TeamId = fixture.TeamId, ActorUserId = fixture.OwnerId, OccurredAt = fixture.Now });
                break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private static async Task<CaptureFixture> SeedAsync(DbContextOptions<NoCtfDbContext> options,
        int questionCount, int repliesPerQuestion, int incidentCount, CancellationToken ct, bool establishBaseline = true)
    {
        var start = DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fixture = new CaptureFixture(Id(10), Id(1), Id(40), Id(41), Id(20), Id(30), Id(80_001), start.AddDays(2),
            Enumerable.Range(0, questionCount).Select(value => Id(1_000 + value)).ToArray(),
            Enumerable.Range(0, incidentCount).Select(value => Id(50_000 + value)).ToArray(), questionCount + incidentCount + 1L);
        await using var db = new NoCtfDbContext(options, new FixedClock(fixture.Now));
        await db.Database.EnsureCreatedAsync(ct);
        db.Users.AddRange(new User { Id = fixture.OwnerId, UserName = "owner", NormalizedUserName = "OWNER",
                Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", PasswordHash = "PROTECTED_PASSWORD" },
            new User { Id = Id(2), UserName = "other", NormalizedUserName = "OTHER",
                Email = "other@example.test", NormalizedEmail = "OTHER@EXAMPLE.TEST", PasswordHash = "PROTECTED_PASSWORD" });
        db.Competitions.Add(new CtfCompetition { Id = fixture.CompetitionId, OwnerId = fixture.OwnerId, Title = "Staff capture corpus",
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf), StartAt = start, EndAt = fixture.Now.AddDays(1) });
        db.Challenges.Add(new CtfChallenge { Id = fixture.TemplateId, OwnerId = fixture.OwnerId, Title = "Challenge", Direction = "Web",
            Description = "PROTECTED_DESCRIPTION", Definition = TestConfigurations.Definition(GameMode.Ctf) });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge { Id = fixture.ChallengeId, CompetitionId = fixture.CompetitionId,
            ChallengeId = fixture.TemplateId, Rules = TestConfigurations.Rules(GameMode.Ctf) });
        db.Teams.AddRange(new Team { Id = fixture.TeamId, CompetitionId = fixture.CompetitionId, Name = "Blue", CaptainId = fixture.OwnerId,
            MemberIds = [fixture.OwnerId], InvitationToken = new string('A', 32), IsBanned = true },
            new Team { Id = fixture.RelatedTeamId, CompetitionId = fixture.CompetitionId, Name = "Red", CaptainId = Id(2),
                MemberIds = [Id(2)], InvitationToken = new string('B', 32) });
        await db.SaveChangesAsync(ct);
        for (var index = 0; index < fixture.QuestionIds.Length; index++)
        {
            var root = fixture.QuestionIds[index]; var at = start.AddMinutes(index);
            db.Notifications.Add(Question(fixture, root, at, CompetitionQuestionParticipantRole.Asker));
            for (var reply = 0; reply < repliesPerQuestion; reply++)
                db.Notifications.Add(Reply(fixture, Id(100_000 + index * 100 + reply), root,
                    reply % 4 == 0 ? CompetitionQuestionParticipantRole.Judge : CompetitionQuestionParticipantRole.Participant, at.AddSeconds(reply + 1)));
        }
        for (var index = 0; index < fixture.IncidentIds.Length; index++)
        {
            db.GameplayFacts.Add(Incident(fixture, fixture.IncidentIds[index], start.AddSeconds(index)));
            if (index % 2 == 1) db.CompetitionEvents.Add(new CheatIncidentDismissedEvent { Id = Id(60_000 + index), CompetitionId = fixture.CompetitionId,
                GameplayFactId = fixture.IncidentIds[index], ActorUserId = fixture.OwnerId, OccurredAt = start.AddHours(2), Reason = "PROTECTED_REASON" });
        }
        db.CompetitionEvents.Add(new TeamBannedEvent { Id = Id(80_000), CompetitionId = fixture.CompetitionId, TeamId = fixture.TeamId,
            ActorUserId = fixture.OwnerId, OccurredAt = start.AddHours(1), Reason = "PROTECTED_REASON" });
        db.CompetitionEvents.Add(new TeamBanAppealSubmittedEvent { Id = fixture.AppealId, CompetitionId = fixture.CompetitionId, TeamId = fixture.TeamId,
            ParentEventId = Id(80_000), ActorUserId = fixture.OwnerId, OccurredAt = start.AddHours(2), Reason = "PROTECTED_REASON" });
        await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
        if (establishBaseline)
        {
            // Seed a canonical existing read model; measured mutations do not include initial synchronization.
            var summaries = await StaffWorkItemProjection.ReadAsync(db, fixture.CompetitionId, ct);
            long sequence = 0;
            foreach (var summary in summaries.OrderBy(value => value.Kind).ThenBy(value => value.Id))
            {
                summary.LastChangedSequence = ++sequence;
                db.StaffWebhookWorkItems.Add(new() { CompetitionId = fixture.CompetitionId, Kind = summary.Kind, ItemId = summary.Id, Summary = summary });
            }
            db.StaffWebhookStreams.Add(new() { CompetitionId = fixture.CompetitionId, Sequence = sequence, LatestBusinessSequence = sequence });
        }
        db.StaffWebhookTargets.Add(new() { Id = Id(70), CompetitionId = fixture.CompetitionId, ApprovedById = fixture.OwnerId,
            Name = "Receiver", EndpointUrl = "https://receiver.example.test/staff", Enabled = true });
        await db.SaveChangesAsync(ct);
        return fixture;
    }

    private static QuestionOpenedNotification Question(CaptureFixture fixture, Guid id, DateTimeOffset at, CompetitionQuestionParticipantRole role) => new()
    {
        Id = id, TargetType = NotificationTargetType.CompetitionCollaborators, TargetId = fixture.CompetitionId,
        SourceId = fixture.OwnerId, TeamId = fixture.TeamId, CompetitionChallengeId = fixture.ChallengeId,
        QuestionSubject = CompetitionQuestionSubject.Challenge, QuestionStatus = CompetitionQuestionStatus.Pending,
        QuestionActorRole = role, SentAt = at, Body = "PROTECTED_QUESTION_BODY"
    };

    private static MessageNotification Reply(CaptureFixture fixture, Guid id, Guid root, CompetitionQuestionParticipantRole role, DateTimeOffset at) => new()
    {
        Id = id, ThreadRootId = root, TargetType = NotificationTargetType.CompetitionCollaborators, TargetId = fixture.CompetitionId,
        SourceId = fixture.OwnerId, QuestionStatus = CompetitionQuestionStatus.Pending, QuestionActorRole = role, SentAt = at, Body = "PROTECTED_REPLY_BODY"
    };

    private static FlagAttemptGameplayFact Incident(CaptureFixture fixture, Guid id, DateTimeOffset at) => new()
    {
        Id = id, CompetitionId = fixture.CompetitionId, CompetitionChallengeId = fixture.ChallengeId, TeamId = fixture.TeamId,
        VictimTeamId = fixture.RelatedTeamId, ActorUserId = fixture.OwnerId, State = GameplayFactState.Completed,
        Result = GameplayFactResult.Rejected, FailureCode = GameplayFactFailureCode.StaticFlagWithoutAttachment,
        Value = "PROTECTED_FLAG", OccurredAt = at, UpdatedAt = at
    };

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainerFixture postgres) => new DbContextOptionsBuilder<NoCtfDbContext>()
        .UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed record CaptureFixture(Guid CompetitionId, Guid OwnerId, Guid TeamId, Guid RelatedTeamId, Guid TemplateId, Guid ChallengeId,
        Guid AppealId, DateTimeOffset Now, Guid[] QuestionIds, Guid[] IncidentIds, long InitialSequence);
    private sealed record CaptureObservation(string CanonicalEvents, int EventCount, double Milliseconds, long AllocatedBytes,
        long ReturnedRows, long ReturnedCells, int[] Collections, IReadOnlyList<ProjectionReadCommand> Commands);
    private enum CaptureChange { ParticipantReply, StaffReply, QuestionClosed, NewThreadWithPendingReply, IncidentDismissed,
        IncidentReasonChanged, IncidentCleared, IncidentRemovedWithReply, NewIncidentAndReply, TeamMetadataWithReply, TemplateMetadataWithReply,
        TeamUnbanned, AppealAccepted }
    private enum MetadataChange { Title, Direction, Team, RelatedTeam, MissingReference }
    private enum BulkIncidentChange { UpdatedAt, Reason, Cleared, Deleted, ResolvedUpdatedAt, MetadataAfterClosure }
}
