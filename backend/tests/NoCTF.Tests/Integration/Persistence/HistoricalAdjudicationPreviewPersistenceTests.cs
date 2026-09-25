using System.Data.Common;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Scoring;
using NoCTF.Infrastructure.GameplayFacts.AdjudicationPreview;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class HistoricalAdjudicationPreviewPersistenceTests
{
    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Blood_parent_must_be_an_adjudication_in_the_same_fact_scope(bool wrongKind, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var parent = Event(fixture, wrongKind ? fixture.LaterFactId : Guid.NewGuid(),
                wrongKind ? CompetitionEventKind.GameplayFactReceived : CompetitionEventKind.GameplayFactAdjudicated,
                fixture.Now.AddSeconds(-1), GameplayFactResult.Correct);
            var blood = Event(fixture, fixture.LaterFactId, CompetitionEventKind.FirstBloodAwarded,
                fixture.Now.AddSeconds(2), GameplayFactResult.Correct);
            blood.ParentEventId = parent.Id;
            db.CompetitionEvents.AddRange(parent, blood);
            await db.SaveChangesAsync(ct);
            var before = await CountsAsync(db, ct);
            var evidence = (await new HistoricalAdjudicationPreviewStore(db).ReadAsync(fixture.CompetitionId, null, null, null, 50, ct)).Items.Single();
            await Assert.That(evidence.Events.All(item => item.GameplayFactId == fixture.LaterFactId)).IsTrue();
            await Assert.That(HistoricalAdjudicationAnalyzer.Analyze(evidence).Differences.Any(item =>
                item.Kind == AdjudicationDifferenceKind.UnexpectedBloodAward && item.Severity == AdjudicationFindingSeverity.Error
                && item.Classification == AdjudicationFindingClassification.IntegrityGap)).IsTrue();
            await Assert.That(await CountsAsync(db, ct)).IsEqualTo(before);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Truncated_eligibility_prefix_does_not_claim_a_complete_history(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            for (var index = 0; index < 65; index++)
                db.CompetitionEvents.Add(new TrackConfigurationUpdatedEvent
                {
                    Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
 Level = CompetitionEventLevel.Information,
                    Visibility = CompetitionEventVisibility.Staff, SubjectType = EntityReferenceKind.Competition,
                    SubjectId = fixture.CompetitionId, OccurredAt = fixture.Now.AddSeconds(index + 2)
                });
            await db.SaveChangesAsync(ct);
            var item = (await new HistoricalAdjudicationPreviewStore(db).ReadAsync(fixture.CompetitionId, null, null, null, 50, ct)).Items.Single();
            await Assert.That(item.Completeness).IsEqualTo(AdjudicationEvidenceCompleteness.Truncated);
            await Assert.That(item.EligibilityEvents!.Count).IsEqualTo(64);
            await Assert.That(HistoricalAdjudicationAnalyzer.Analyze(item).Differences
                .Any(finding => finding.Classification == AdjudicationFindingClassification.InsufficientEvidence)).IsTrue();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Event_prefix_is_bounded_without_deduplicating_legitimate_rejudgements(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            for (var index = 0; index < 140; index++)
                db.CompetitionEvents.Add(Event(fixture, fixture.LaterFactId, CompetitionEventKind.GameplayFactAdjudicated,
                    fixture.Now.AddSeconds(index + 2), GameplayFactResult.Correct));
            await db.SaveChangesAsync(ct);
            var before = await CountsAsync(db, ct);
            var evidence = await new HistoricalAdjudicationPreviewStore(db).ReadAsync(fixture.CompetitionId, null, null, null, 50, ct);
            var item = evidence.Items.Single();
            await Assert.That(item.Events.Count).IsEqualTo(128);
            await Assert.That(item.Events.Select(row => row.EventId).Distinct().Count()).IsEqualTo(128);
            await Assert.That(item.Completeness).IsEqualTo(AdjudicationEvidenceCompleteness.Truncated);
            var analysis = HistoricalAdjudicationAnalyzer.Analyze(item);
            await Assert.That(analysis.LatestEffectiveAdjudication!.Result).IsEqualTo(GameplayFactResult.Correct);
            await Assert.That(analysis.Differences.Any(row => row.Kind == AdjudicationDifferenceKind.MissingBloodAward)).IsFalse();
            await Assert.That(analysis.Differences.Any(row => row.Severity == AdjudicationFindingSeverity.Error)).IsFalse();
            await Assert.That(await CountsAsync(db, ct)).IsEqualTo(before);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Track_changes_are_information_and_patch_completion_uses_the_current_interaction(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var tracks = CompetitionTrackConfiguration.DefaultFor(GameMode.Ctf);
            var guest = tracks.DefaultTrack with { Key = "guest", Name = "Guest", IsDefault = false, EarnsBlood = false };
            var competition = await db.Competitions.SingleAsync(row => row.Id == fixture.CompetitionId, ct);
            competition.TracksEnabled = true;
            competition.Tracks = CompetitionTrackConfiguration.ToPersisted(
                tracks with { Tracks = [tracks.DefaultTrack, guest] }, fixture.CompetitionId);
            await db.SaveChangesAsync(ct);
            await db.Teams.Where(row => row.Id == fixture.FirstTeamId).ExecuteUpdateAsync(update => update.SetProperty(row => row.TrackKey, "guest"), ct);
            db.CompetitionEvents.Add(new TeamTrackChangedEvent
            {
                Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
                Level = CompetitionEventLevel.Information, Visibility = CompetitionEventVisibility.Staff,
                SubjectType = EntityReferenceKind.Team, SubjectId = fixture.FirstTeamId,
                TeamId = fixture.FirstTeamId,
                OccurredAt = fixture.Now.AddSeconds(5), TrackKey = "guest", PreviousTrackKey = "default"
            });
            await db.SaveChangesAsync(ct);
            var preview = new PreviewHistoricalAdjudicationDifferences(new HistoricalAdjudicationPreviewStore(db));
            await Assert.That((await preview.ExecuteAsync(fixture.CompetitionId, null, null, null, 20, ct)).Items).IsEmpty();
            var information = await preview.ExecuteAsync(fixture.CompetitionId, null, null, null, 20, true, ct);
            await Assert.That(information.Items.Single().Differences.All(row => row.Severity == AdjudicationFindingSeverity.Information)).IsTrue();
            await Assert.That(information.Items.Single().EligibilityEvents.Count).IsEqualTo(1);

            var internalTrack = guest with
            {
                IsInternal = true, IsPublicSelectable = false, EarnsScore = false, EarnsBlood = false,
                AffectsDynamicChallengeScore = false, VisibleOnLeaderboard = false, AffectsCompetitiveResults = false
            };
            competition.Tracks = CompetitionTrackConfiguration.ToPersisted(
                tracks with { Tracks = [tracks.DefaultTrack, internalTrack] }, fixture.CompetitionId);
            await db.SaveChangesAsync(ct);
            var evidenceStore = new HistoricalAdjudicationPreviewStore(db);
            await Assert.That((await evidenceStore.ReadRestrictedAsync(fixture.CompetitionId, null, null, null, 20, ct)).Items).IsEmpty();
            await Assert.That((await evidenceStore.ReadAsync(fixture.CompetitionId, null, null, null, 20, ct)).Items.Count).IsEqualTo(1);
            await Assert.That(await evidenceStore.ReadEventsAsync(fixture.CompetitionId, fixture.LaterFactId, null, null, 1, false, ct)).IsNull();
            var firstPage = await evidenceStore.ReadEventsAsync(fixture.CompetitionId, fixture.LaterFactId, null, null, 1, true, ct);
            await Assert.That(firstPage!.NextBeforeId).IsNotNull();
            var secondPage = await evidenceStore.ReadEventsAsync(fixture.CompetitionId, fixture.LaterFactId,
                firstPage.NextBeforeOccurredAt, firstPage.NextBeforeId, 1, true, ct);
            await Assert.That(firstPage.Events[0].EventId).IsNotEqualTo(secondPage!.Events[0].EventId);
            var access = new CompetitionModerationAuthorizer(db);
            await Assert.That(await access.CanReadInternalHistoricalAuditAsync(fixture.ObserverId, fixture.CompetitionId, ct)).IsFalse();
            await Assert.That(await access.CanReadInternalHistoricalAuditAsync(fixture.JudgeId, fixture.CompetitionId, ct)).IsTrue();

            await db.Competitions.Where(row => row.Id == fixture.CompetitionId).ExecuteUpdateAsync(update => update.SetProperty(row => row.TracksEnabled, false), ct);
            var templateId = await db.CompetitionChallenges.Where(row => row.Id == fixture.CompetitionChallengeId).Select(row => row.ChallengeId).SingleAsync(ct);
            var template = await db.Challenges.SingleAsync(row => row.Id == templateId, ct);
            ((CtfChallengeDefinition)template.Definition!).InteractionKind =
                CtfInteractionKind.PatchVerification;
            await db.SaveChangesAsync(ct);
            // Archive content is outside this read-only projection; only the typed reference identity is relevant.
            var currentFact = await db.GameplayFacts.SingleAsync(
                row => row.Id == fixture.LaterFactId,
                ct);
            db.GameplayFacts.Remove(currentFact);
            await db.SaveChangesAsync(ct);
            db.GameplayFacts.Add(new FixAttemptGameplayFact
            {
                Id = currentFact.Id,
                CompetitionId = currentFact.CompetitionId,
                CompetitionChallengeId = currentFact.CompetitionChallengeId,
                TeamId = currentFact.TeamId,
                ActorUserId = currentFact.ActorUserId,
                ReferenceKind = GameplayFactReferenceKind.PatchUpload,
                ReferenceId = Guid.NewGuid(),
                State = currentFact.State,
                Result = currentFact.Result,
                FailureCode = currentFact.FailureCode,
                OccurredAt = currentFact.OccurredAt,
                UpdatedAt = currentFact.UpdatedAt
            });
            await db.SaveChangesAsync(ct);
            var patch = (await new HistoricalAdjudicationPreviewStore(db).ReadAsync(fixture.CompetitionId, null, null, null, 20, ct)).Items.Single();
            await Assert.That(patch.GameplayFactKind).IsEqualTo(GameplayFactKind.FixAttempt);
            await Assert.That(patch.MatchesCurrentInteraction).IsTrue();
            await Assert.That(HistoricalAdjudicationAnalyzer.Analyze(patch).CurrentProjectedBloodRank).IsEqualTo(LeaderboardBloodRank.First);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Preview_detects_out_of_order_and_duplicate_blood_without_writing(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);

            await using (var initial = new NoCtfDbContext(options))
            {
                var initialPreview = new PreviewHistoricalAdjudicationDifferences(
                    new HistoricalAdjudicationPreviewStore(initial));
                var normal = await initialPreview.ExecuteAsync(
                    fixture.CompetitionId, null, null, null, 20, cancellationToken);
                await Assert.That(normal.Items).IsEmpty();
            }

            await SeedHistoricalDifferencesAsync(options, fixture, cancellationToken);
            await using var db = new NoCtfDbContext(options);
            var authorizer = new CompetitionModerationAuthorizer(db);
            foreach (var staffId in new[]
                     {
                         fixture.OwnerId,
                         fixture.ManagerId,
                         fixture.JudgeId,
                         fixture.ObserverId,
                         fixture.AdministratorId
                     })
            {
                await Assert.That(await authorizer.CanObserveAsync(
                    staffId, fixture.CompetitionId, cancellationToken)).IsTrue();
            }
            await Assert.That(await authorizer.CanObserveAsync(
                fixture.FirstUserId, fixture.CompetitionId, cancellationToken)).IsFalse();
            var before = await CountsAsync(db, cancellationToken);
            var preview = new PreviewHistoricalAdjudicationDifferences(
                new HistoricalAdjudicationPreviewStore(db));

            var page = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);

            var after = await CountsAsync(db, cancellationToken);
            await Assert.That(after).IsEqualTo(before);
            var later = page.Items.Single(item => item.GameplayFactId == fixture.LaterFactId);
            var earlier = page.Items.Single(item => item.GameplayFactId == fixture.EarlierFactId);
            await Assert.That(earlier.OccurredAt).IsEqualTo(later.OccurredAt);
            await Assert.That(string.CompareOrdinal(
                fixture.EarlierFactId.ToString("N"),
                fixture.LaterFactId.ToString("N"))).IsLessThan(0);
            await Assert.That(later.DeterministicExpectedResult).IsEqualTo(GameplayFactResult.Wrong);
            await Assert.That(later.Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.DuplicateBloodAward);
            await Assert.That(later.Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.HistoricalResultChanged);
            await Assert.That(later.Differences
                .Single(item => item.Kind == AdjudicationDifferenceKind.HistoricalResultChanged)
                .Certainty).IsEqualTo(AdjudicationDifferenceCertainty.Deterministic);
            await Assert.That(page.Items.Single(item => item.GameplayFactId == fixture.EarlierFactId)
                .Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.MissingBloodAward);
            await Assert.That(page.Items.Single(item => item.GameplayFactId == fixture.EarlierFactId)
                .Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.MissingAdjudicationRecord);

            var firstPage = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 1, cancellationToken);
            await Assert.That(firstPage.Items).Count().IsEqualTo(1);
            await Assert.That(firstPage.NextBeforeOccurredAt).IsNotNull();
            await Assert.That(firstPage.NextBeforeId).IsNotNull();
            var secondPage = await preview.ExecuteAsync(
                fixture.CompetitionId,
                null,
                firstPage.NextBeforeOccurredAt,
                firstPage.NextBeforeId,
                1,
                cancellationToken);
            await Assert.That(secondPage.Items).Count().IsEqualTo(1);
            await Assert.That(firstPage.Items.Concat(secondPage.Items)
                .Select(item => item.GameplayFactId)
                .Distinct()).Count().IsEqualTo(2);

            await db.Teams.Where(item => item.Id == fixture.FirstTeamId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsBanned, true),
                    cancellationToken);
            var ineligiblePreview = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);
            var ineligibleEarlier = ineligiblePreview.Items
                .Single(item => item.GameplayFactId == fixture.EarlierFactId);
            await Assert.That(ineligibleEarlier.DeterministicExpectedBloodRank).IsNull();
            await Assert.That(ineligibleEarlier.Differences.Select(item => item.Kind))
                .DoesNotContain(AdjudicationDifferenceKind.TeamEligibilityHistoryRequiresReview);
            // Missing adjudication is still an integrity issue; no award means no eligibility conflict.
            await Assert.That(ineligibleEarlier.Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.MissingAdjudicationRecord);
            await Assert.That(ineligiblePreview.Items.Single(item => item.GameplayFactId == fixture.LaterFactId)
                .Differences.Select(item => item.Kind)).Contains(AdjudicationDifferenceKind.TeamEligibilityHistoryRequiresReview);
            await Assert.That(ineligibleEarlier.Differences.Select(item => item.Kind))
                .DoesNotContain(AdjudicationDifferenceKind.MissingBloodAward);
            await db.Teams.Where(item => item.Id == fixture.FirstTeamId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsBanned, false),
                    cancellationToken);

            await SeedIneligibleEarlierTeamAsync(options, fixture, cancellationToken);
            var priorEligibilityPreview = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);
            var priorAffected = priorEligibilityPreview.Items
                .Single(item => item.GameplayFactId == fixture.EarlierFactId);
            await Assert.That(priorAffected.DeterministicExpectedBloodRank).IsNull();
            await Assert.That(priorAffected.Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.TeamEligibilityHistoryRequiresReview);
            await Assert.That(priorAffected.Differences.Select(item => item.Kind))
                .DoesNotContain(AdjudicationDifferenceKind.MissingBloodAward);

            var missingCompetition = await preview.ExecuteAsync(
                Guid.NewGuid(), null, null, null, 20, cancellationToken);
            await Assert.That(missingCompetition.State)
                .IsEqualTo(HistoricalAdjudicationPreviewReadState.CompetitionNotFound);
            await db.Competitions.Where(item => item.Id == fixture.CompetitionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.DeletedAt, fixture.Now), cancellationToken);
            foreach (var archivedStaffId in new[]
                     {
                         fixture.OwnerId,
                         fixture.ManagerId,
                         fixture.JudgeId,
                         fixture.ObserverId,
                         fixture.AdministratorId
                     })
            {
                await Assert.That(await authorizer.CanReadHistoricalAuditAsync(
                    archivedStaffId, fixture.CompetitionId, cancellationToken)).IsTrue();
            }
            await Assert.That(await authorizer.CanReadHistoricalAuditAsync(
                fixture.FirstUserId, fixture.CompetitionId, cancellationToken)).IsFalse();
            foreach (var archivedCollaboratorId in new[]
                     {
                         fixture.OwnerId,
                         fixture.ManagerId,
                         fixture.JudgeId,
                         fixture.ObserverId
                     })
            {
                await Assert.That(await authorizer.CanObserveAsync(
                    archivedCollaboratorId, fixture.CompetitionId, cancellationToken)).IsFalse();
                await Assert.That(await authorizer.CanJudgeAsync(
                    archivedCollaboratorId, fixture.CompetitionId, cancellationToken)).IsFalse();
                await Assert.That(await authorizer.CanModerateAsync(
                    archivedCollaboratorId, fixture.CompetitionId, cancellationToken)).IsFalse();
            }

            var archivedCompetition = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);
            await Assert.That(archivedCompetition.State)
                .IsEqualTo(HistoricalAdjudicationPreviewReadState.Available);
            await Assert.That(archivedCompetition.Items.Select(item => item.GameplayFactId))
                .IsEquivalentTo([fixture.EarlierFactId, fixture.LaterFactId]);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Preview_uses_one_repeatable_snapshot_and_a_bounded_query_count(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);
            var pause = new EventQueryPauseInterceptor();
            var snapshotOptions = Options(postgres, pause);

            await using var previewDb = new NoCtfDbContext(snapshotOptions);
            var preview = new PreviewHistoricalAdjudicationDifferences(
                new HistoricalAdjudicationPreviewStore(previewDb));
            var previewTask = preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);
            await pause.EventQueryStarting.WaitAsync(cancellationToken);

            await using (var writer = new NoCtfDbContext(options))
            {
                await writer.GameplayFacts.Where(fact => fact.Id == fixture.LaterFactId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        fact => fact.Result, GameplayFactResult.Wrong), cancellationToken);
                writer.CompetitionEvents.Add(Event(
                    fixture,
                    fixture.LaterFactId,
                    CompetitionEventKind.GameplayFactAdjudicated,
                    fixture.Now.AddSeconds(2),
                    GameplayFactResult.Wrong));
                await writer.SaveChangesAsync(cancellationToken);
            }

            pause.Continue();
            var snapshotPage = await previewTask;
            await Assert.That(snapshotPage.Items).IsEmpty();

            await SeedLargeHistoryAsync(options, fixture, cancellationToken);
            var counter = new QueryCountingInterceptor();
            await using var countedDb = new NoCtfDbContext(Options(postgres, counter));
            var countedPreview = new PreviewHistoricalAdjudicationDifferences(
                new HistoricalAdjudicationPreviewStore(countedDb));
            var countedPage = await countedPreview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);

            await Assert.That(countedPage.Items).Count().IsEqualTo(20);
            // Competition, official window, candidates, challenge definitions,
            // combined first-completion/team qualification, and audit evidence.
            // Eligibility adjustment evidence shares the bounded audit-event round trip.
            await Assert.That(counter.ReaderCommandCount).IsLessThanOrEqualTo(8);
            var measurements = Environment.GetEnvironmentVariable("NOCTF_CAPACITY_MEASUREMENTS");
            if (!string.IsNullOrWhiteSpace(measurements))
            {
                var milliseconds = new List<double>();
                var queries = new List<int>();
                var queryMilliseconds = new List<double[]>();
                var allocatedBytes = new List<long>();
                for (var iteration = 0; iteration < 60; iteration++)
                {
                    var initialCount = counter.ReaderCommandCount;
                    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var measured = await countedPreview.ExecuteAsync(fixture.CompetitionId, null, null, null, 500, cancellationToken);
                    await Assert.That(measured.Items.Count).IsEqualTo(500);
                    var elapsedMs = watch.Elapsed.TotalMilliseconds;
                    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
                    if (iteration < 10) continue;
                    milliseconds.Add(elapsedMs);
                    allocatedBytes.Add(allocated);
                    queries.Add(counter.ReaderCommandCount - initialCount);
                    queryMilliseconds.Add(counter.ReaderMilliseconds.Skip(initialCount).ToArray());
                }
                var evidence = await new HistoricalAdjudicationPreviewStore(countedDb).ReadAsync(fixture.CompetitionId, null, null, null, 500, cancellationToken);
                var analyzeWatch = System.Diagnostics.Stopwatch.StartNew();
                for (var repeat = 0; repeat < 100; repeat++)
                    foreach (var item in evidence.Items) _ = HistoricalAdjudicationAnalyzer.Analyze(item);
                var analyzePageMs = analyzeWatch.Elapsed.TotalMilliseconds / 100;
                Directory.CreateDirectory(measurements);
                await File.WriteAllTextAsync(Path.Combine(measurements, "preview-scan.json"), JsonSerializer.Serialize(new
                {
                    corpusFacts = 601, pageLimit = 500, warmup = 10, milliseconds, queries, queryMilliseconds, analyzePageMs, allocatedBytes
                }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            }
        });
    }

    private static async Task SeedHistoricalDifferencesAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        db.GameplayFacts.Add(new FlagAttemptGameplayFact
        {
            Id = fixture.EarlierFactId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.FirstTeamId,
            ActorUserId = fixture.FirstUserId,
            Value = "flag{preview}",
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{preview}")),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct,
            OccurredAt = fixture.Now.AddSeconds(1),
            UpdatedAt = fixture.Now.AddSeconds(1)
        });
        db.CompetitionEvents.AddRange(
            Event(fixture, fixture.LaterFactId, CompetitionEventKind.FirstBloodAwarded,
                fixture.Now.AddSeconds(1), GameplayFactResult.Correct),
            Event(fixture, fixture.LaterFactId, CompetitionEventKind.GameplayFactAdjudicated,
                fixture.Now.AddSeconds(2), GameplayFactResult.Wrong));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedIneligibleEarlierTeamAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken ct)
    {
        var teamId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var factId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await using var db = new NoCtfDbContext(options);
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = fixture.CompetitionId,
            Name = "Ineligible prior team",
            CaptainId = fixture.OwnerId,
            MemberIds = [fixture.OwnerId],
            InvitationToken = new string('b', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            IsBanned = true,
            RegisteredAt = fixture.Now
        });
        db.GameplayFacts.Add(new FlagAttemptGameplayFact
        {
            Id = factId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = teamId,
            ActorUserId = fixture.OwnerId,
            Value = "flag{ineligible-prior}",
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{ineligible-prior}")),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct,
            OccurredAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        db.CompetitionEvents.Add(Event(
            fixture,
            factId,
            CompetitionEventKind.GameplayFactAdjudicated,
            fixture.Now,
            GameplayFactResult.Correct));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLargeHistoryAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        for (var index = 0; index < 600; index++)
        {
            var value = $"flag{{bounded-query-{index}}}";
            db.GameplayFacts.Add(new FlagAttemptGameplayFact
            {
                Id = Guid.CreateVersion7(),
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = fixture.FirstTeamId,
                ActorUserId = fixture.FirstUserId,
                Value = value,
                ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(value)),
                State = GameplayFactState.Completed,
                Result = GameplayFactResult.Wrong,
                OccurredAt = fixture.Now.AddSeconds(index + 1),
                UpdatedAt = fixture.Now.AddSeconds(index + 1)
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static CompetitionEvent Event(
        Fixture fixture,
        Guid factId,
        CompetitionEventKind kind,
        DateTimeOffset occurredAt,
        GameplayFactResult result)
    {
        var @event = CompetitionEventGeneratedCatalog.Create(kind);
        @event.Id = Guid.CreateVersion7(occurredAt);
        @event.CompetitionId = fixture.CompetitionId;
        @event.Level = CompetitionEventLevel.Information;
        @event.Visibility = CompetitionEventVisibility.Staff;
        @event.SubjectType = EntityReferenceKind.GameplayFact;
        @event.SubjectId = factId;
        @event.RelatedType = EntityReferenceKind.Team;
        @event.RelatedId = fixture.FirstTeamId;
        @event.GameplayFactId = factId;
        @event.GameplayFactState = GameplayFactState.Completed;
        @event.GameplayFactResult = result;
        @event.CompetitionChallengeId = fixture.CompetitionChallengeId;
        @event.OccurredAt = occurredAt;
        return @event;
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var ownerId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var managerId = Guid.CreateVersion7();
        var judgeId = Guid.CreateVersion7();
        var observerId = Guid.CreateVersion7();
        var administratorId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var fixture = new Fixture(
            now,
            competitionId,
            competitionChallengeId,
            teamId,
            userId,
            ownerId,
            managerId,
            judgeId,
            observerId,
            administratorId,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"));
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        db.Users.AddRange(
            User(ownerId, "preview-owner", UserRole.Organizer, now),
            User(managerId, "preview-manager", UserRole.Organizer, now),
            User(judgeId, "preview-judge", UserRole.Organizer, now),
            User(observerId, "preview-observer", UserRole.Organizer, now),
            User(userId, "preview-player", UserRole.User, now),
            User(administratorId, "preview-admin", UserRole.Administrator, now));
        db.Competitions.Add(new CtfCompetition
        {
            Id = competitionId,
            OwnerId = ownerId,
            ManagerIds = [managerId],
            JudgeIds = [judgeId],
            ObserverIds = [observerId],
            Title = "Adjudication preview",
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32],
            StartAt = now,
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Preview Team",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new CtfChallenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "Preview challenge",
            Direction = "Web",
            Definition = TestConfigurations.Definition(GameMode.Ctf),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            Rules = TestConfigurations.Rules(GameMode.Ctf),
            UpdatedAt = now
        });
        db.GameplayFacts.Add(new FlagAttemptGameplayFact
        {
            Id = fixture.LaterFactId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = userId,
            Value = "flag{preview}",
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{preview}")),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct,
            OccurredAt = now.AddSeconds(1),
            UpdatedAt = now.AddSeconds(1)
        });
        var adjudication = Event(
            fixture,
            fixture.LaterFactId,
            CompetitionEventKind.GameplayFactAdjudicated,
            now.AddSeconds(1),
            GameplayFactResult.Correct);
        var blood = Event(
            fixture,
            fixture.LaterFactId,
            CompetitionEventKind.FirstBloodAwarded,
            now.AddSeconds(1),
            GameplayFactResult.Correct);
        blood.ParentEventId = adjudication.Id;
        db.CompetitionEvents.AddRange(adjudication, blood);
        await db.SaveChangesAsync(ct);
        return fixture;
    }

    private static User User(Guid id, string name, UserRole role, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        Kind = UserKind.Human,
        Role = role,
        EmailVerifiedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static async Task<(int Facts, int Events, int Notifications)> CountsAsync(
        NoCtfDbContext db,
        CancellationToken ct) =>
        (await db.GameplayFacts.CountAsync(ct),
            await db.CompetitionEvents.CountAsync(ct),
            await db.Notifications.CountAsync(ct));

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken ct)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("noctf_adjudication_preview")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(ct);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> Options(
        PostgreSqlContainer postgres,
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention();
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return builder.Options;
    }

    private sealed class EventQueryPauseInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource eventQueryStarting = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource continuation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int paused;

        public Task EventQueryStarting => eventQueryStarting.Task;

        public void Continue() => continuation.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("competition_events", StringComparison.OrdinalIgnoreCase)
                && Interlocked.CompareExchange(ref paused, 1, 0) == 0)
            {
                eventQueryStarting.TrySetResult();
                await continuation.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class QueryCountingInterceptor : DbCommandInterceptor
    {
        private int readerCommandCount;

        public int ReaderCommandCount => Volatile.Read(ref readerCommandCount);
        public List<double> ReaderMilliseconds { get; } = [];
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            ReaderMilliseconds.Add(eventData.Duration.TotalMilliseconds);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref readerCommandCount);
            return ValueTask.FromResult(result);
        }
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid FirstTeamId,
        Guid FirstUserId,
        Guid OwnerId,
        Guid ManagerId,
        Guid JudgeId,
        Guid ObserverId,
        Guid AdministratorId,
        Guid EarlierFactId,
        Guid LaterFactId);

}
