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
            var configuration = JsonSerializer.Serialize(tracks with { Tracks = [tracks.DefaultTrack, guest] }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await db.Competitions.Where(row => row.Id == fixture.CompetitionId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.TracksEnabled, true).SetProperty(row => row.TrackConfigurationJson, configuration), ct);
            await db.Teams.Where(row => row.Id == fixture.FirstTeamId).ExecuteUpdateAsync(update => update.SetProperty(row => row.TrackKey, "guest"), ct);
            db.CompetitionEvents.Add(new CompetitionEvent
            {
                Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId, Kind = CompetitionEventKind.TeamTrackChanged,
                Level = CompetitionEventLevel.Information, Visibility = CompetitionEventVisibility.Staff,
                SubjectType = EntityReferenceKind.Team, SubjectId = fixture.FirstTeamId,
                OccurredAt = fixture.Now.AddSeconds(5), PayloadJson = "{\"schemaVersion\":1,\"trackKey\":\"guest\",\"previousTrackKey\":\"default\"}"
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
            var internalConfiguration = JsonSerializer.Serialize(tracks with { Tracks = [tracks.DefaultTrack, internalTrack] }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await db.Competitions.Where(row => row.Id == fixture.CompetitionId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.TrackConfigurationJson, internalConfiguration), ct);
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
            await db.Challenges.Where(row => row.Id == templateId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.DefinitionJson, "{\"schemaVersion\":3,\"interactionKind\":1}"), ct);
            // Archive content is outside this read-only projection; only the typed reference identity is relevant.
            await db.GameplayFacts.Where(row => row.Id == fixture.LaterFactId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.Kind, GameplayFactKind.FixAttempt).SetProperty(row => row.Value, (string?)null)
                    .SetProperty(row => row.ValueSha256, (byte[]?)null)
                    .SetProperty(row => row.ReferenceKind, (GameplayFactReferenceKind?)GameplayFactReferenceKind.PatchUpload)
                    .SetProperty(row => row.ReferenceId, (Guid?)Guid.NewGuid()), ct);
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
            await Assert.That(later.Differences.Select(item => item.Kind))
                .DoesNotContain(AdjudicationDifferenceKind.CurrentCorrectShouldBeDuplicate);
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
                .Contains(AdjudicationDifferenceKind.TeamEligibilityHistoryRequiresReview);
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

            await db.GameplayFacts.Where(item => item.Id == fixture.LaterFactId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.Kind, GameplayFactKind.BreakAttempt), cancellationToken);
            var crossKindPreview = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);
            await Assert.That(crossKindPreview.Items.Select(item => item.GameplayFactId))
                .DoesNotContain(fixture.LaterFactId);
            await db.GameplayFacts.Where(item => item.Id == fixture.LaterFactId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.Kind, GameplayFactKind.FlagAttempt), cancellationToken);

            await db.Competitions.Where(item => item.Id == fixture.CompetitionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Mode, GameMode.Awdp),
                    cancellationToken);
            await db.GameplayFacts.Where(item => item.CompetitionId == fixture.CompetitionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.Kind, GameplayFactKind.BreakAttempt), cancellationToken);
            await db.CompetitionEvents.Where(item => item.CompetitionId == fixture.CompetitionId
                    && (item.Kind == CompetitionEventKind.FirstBloodAwarded
                        || item.Kind == CompetitionEventKind.SecondBloodAwarded
                        || item.Kind == CompetitionEventKind.ThirdBloodAwarded))
                .ExecuteDeleteAsync(cancellationToken);
            var breakPreview = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);
            await Assert.That(breakPreview.Items
                    .SelectMany(item => item.Differences)
                    .Select(difference => difference.Kind))
                .DoesNotContain(AdjudicationDifferenceKind.CurrentCorrectShouldBeDuplicate);
            await Assert.That(breakPreview.Items
                    .SelectMany(item => item.Differences)
                    .Select(difference => difference.Kind))
                .DoesNotContain(AdjudicationDifferenceKind.DuplicateWithoutCurrentPredecessor);
            await Assert.That(breakPreview.Items
                    .SelectMany(item => item.Differences)
                    .Select(difference => difference.Kind))
                .DoesNotContain(AdjudicationDifferenceKind.MissingBloodAward);
            await Assert.That(breakPreview.Items
                    .SelectMany(item => item.Differences)
                    .Select(difference => difference.Kind))
                .DoesNotContain(AdjudicationDifferenceKind.UnexpectedBloodAward);
            await Assert.That(breakPreview.Items
                    .Single(item => item.GameplayFactId == fixture.LaterFactId)
                    .Differences.Select(difference => difference.Kind))
                .Contains(AdjudicationDifferenceKind.HistoricalResultChanged);
            await Assert.That(breakPreview.Items
                    .Single(item => item.GameplayFactId == fixture.EarlierFactId)
                    .Differences.Select(difference => difference.Kind))
                .Contains(AdjudicationDifferenceKind.MissingAdjudicationRecord);

            foreach (var unsupportedMode in new[] { GameMode.Awd, GameMode.Koh })
            {
                await db.Competitions.Where(item => item.Id == fixture.CompetitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        item => item.Mode, unsupportedMode), cancellationToken);
                var unsupportedPreview = await preview.ExecuteAsync(
                    fixture.CompetitionId, null, null, null, 20, cancellationToken);
                await Assert.That(unsupportedPreview.Items).IsEmpty();
            }

            var missingCompetition = await preview.ExecuteAsync(
                Guid.NewGuid(), null, null, null, 20, cancellationToken);
            await Assert.That(missingCompetition.State)
                .IsEqualTo(HistoricalAdjudicationPreviewReadState.CompetitionNotFound);
            await db.Competitions.Where(item => item.Id == fixture.CompetitionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Mode, GameMode.Awdp)
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
    public async Task Preview_marks_only_legacy_awdp_duplicate_achievements_as_correct_without_writing(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            await using (var migrationDb = new NoCtfDbContext(options))
                await migrationDb.Database.EnsureCreatedAsync(cancellationToken);
            var fixtures = new[]
            {
                await SeedAwdpHistoryAsync(options, "legacy-a", 'm', cancellationToken),
                await SeedAwdpHistoryAsync(options, "legacy-b", 'p', cancellationToken)
            };

            foreach (var fixture in fixtures)
            {
                await using var db = new NoCtfDbContext(options);
                var before = await CountsAsync(db, cancellationToken);
                var preview = new PreviewHistoricalAdjudicationDifferences(
                    new HistoricalAdjudicationPreviewStore(db));

                var page = await preview.ExecuteAsync(
                    fixture.CompetitionId, null, null, null, 20, cancellationToken);

                await Assert.That(await CountsAsync(db, cancellationToken)).IsEqualTo(before);
                await Assert.That(page.Items.Select(item => item.GameplayFactId).ToHashSet())
                    .IsEquivalentTo(new HashSet<Guid>
                    {
                        fixture.SameRoundLegacyDuplicateFactId,
                        fixture.CrossRoundLegacyDuplicateFactId
                    });
                foreach (var item in page.Items)
                {
                    await Assert.That(item.GameplayFactKind)
                        .IsEqualTo(GameplayFactKind.BreakAttempt);
                    await Assert.That(item.CurrentResult)
                        .IsEqualTo(GameplayFactResult.Duplicate);
                    await Assert.That(item.DeterministicExpectedResult)
                        .IsEqualTo(GameplayFactResult.Correct);
                    await Assert.That(item.DeterministicExpectedBloodRank).IsNull();
                    await Assert.That(item.RecordedBloodRanks).IsEmpty();
                    await Assert.That(item.Differences).Count().IsEqualTo(1);
                    await Assert.That(item.Differences[0].Kind)
                        .IsEqualTo(AdjudicationDifferenceKind.CurrentDuplicateShouldBeCorrect);
                    await Assert.That(item.Differences[0].Certainty)
                        .IsEqualTo(AdjudicationDifferenceCertainty.Deterministic);
                }
            }
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
            // Competition metadata, lifecycle-derived official window, candidates,
            // first-correct facts, audit events, teams, and challenge titles.
            // Eligibility adjustment evidence is one additional bounded query.
            await Assert.That(counter.ReaderCommandCount).IsLessThanOrEqualTo(8);
        });
    }

    private static async Task SeedHistoricalDifferencesAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = fixture.EarlierFactId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.FirstTeamId,
            ActorUserId = fixture.FirstUserId,
            Kind = GameplayFactKind.FlagAttempt,
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

    private static async Task<AwdpFixture> SeedAwdpHistoryAsync(
        DbContextOptions<NoCtfDbContext> options,
        string fixtureName,
        char tokenCharacter,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var ownerId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var initialCorrectId = Guid.CreateVersion7(now);
        var sameRoundDuplicateId = Guid.CreateVersion7(now.AddSeconds(30));
        var crossRoundDuplicateId = Guid.CreateVersion7(now.AddSeconds(90));
        var currentCorrectId = Guid.CreateVersion7(now.AddSeconds(120));
        var otherDuplicateId = Guid.CreateVersion7(now.AddSeconds(150));
        var suffix = fixtureName;
        var configuration = new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            60,
            new(50, 50, 2, ScoreDecayMode.Fixed),
            new(50, 50, 2, ScoreDecayMode.Fixed));
        await using var db = new NoCtfDbContext(options);
        db.Users.AddRange(
            User(ownerId, $"awdp-preview-owner-{suffix}", UserRole.Organizer, now),
            User(userId, $"awdp-preview-player-{suffix}", UserRole.User, now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = $"AWDP {fixtureName} adjudication preview",
            Mode = GameMode.Awdp,
            ConfigurationJson = JsonSerializer.Serialize(configuration),
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
            Name = $"AWDP {fixtureName} Team",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string(tokenCharacter, 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Title = $"AWDP {fixtureName} challenge",
            Direction = "Pwn",
            DefinitionJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = "{}",
            UpdatedAt = now
        });
        var facts = new[]
        {
            AwdpFact(initialCorrectId, GameplayFactResult.Correct, null, now),
            AwdpFact(
                sameRoundDuplicateId,
                GameplayFactResult.Duplicate,
                GameplayFactFailureCode.DuplicateAchievement,
                now.AddSeconds(30)),
            AwdpFact(
                crossRoundDuplicateId,
                GameplayFactResult.Duplicate,
                GameplayFactFailureCode.DuplicateAchievement,
                now.AddSeconds(90)),
            AwdpFact(currentCorrectId, GameplayFactResult.Correct, null, now.AddSeconds(120)),
            AwdpFact(
                otherDuplicateId,
                GameplayFactResult.Duplicate,
                GameplayFactFailureCode.DuplicateAttack,
                now.AddSeconds(150))
        };
        db.GameplayFacts.AddRange(facts);
        db.CompetitionEvents.AddRange(facts.Select(fact => AdjudicationEvent(
            competitionId,
            competitionChallengeId,
            teamId,
            fact.Id,
            fact.OccurredAt,
            fact.Result!.Value)));
        await db.SaveChangesAsync(ct);
        return new(
            competitionId,
            sameRoundDuplicateId,
            crossRoundDuplicateId,
            currentCorrectId,
            otherDuplicateId);

        GameplayFact AwdpFact(
            Guid factId,
            GameplayFactResult result,
            GameplayFactFailureCode? failureCode,
            DateTimeOffset occurredAt)
        {
            var value = $"flag{{awdp-preview-{suffix}-{factId:N}}}";
            return new()
            {
                Id = factId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                ActorUserId = userId,
                Kind = GameplayFactKind.BreakAttempt,
                Value = value,
                ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(value)),
                State = GameplayFactState.Completed,
                Result = result,
                FailureCode = failureCode,
                OccurredAt = occurredAt,
                UpdatedAt = occurredAt
            };
        }
    }

    private static CompetitionEvent AdjudicationEvent(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        Guid factId,
        DateTimeOffset occurredAt,
        GameplayFactResult result) => new()
    {
        Id = Guid.CreateVersion7(occurredAt),
        CompetitionId = competitionId,
        Kind = CompetitionEventKind.GameplayFactAdjudicated,
        Level = CompetitionEventLevel.Information,
        Visibility = CompetitionEventVisibility.Staff,
        SubjectType = EntityReferenceKind.GameplayFact,
        SubjectId = factId,
        RelatedType = EntityReferenceKind.Team,
        RelatedId = teamId,
        PayloadJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            gameplayFactState = GameplayFactState.Completed.ToString(),
            gameplayFactResult = result.ToString(),
            competitionChallengeId
        }),
        OccurredAt = occurredAt
    };

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
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = factId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = teamId,
            ActorUserId = fixture.OwnerId,
            Kind = GameplayFactKind.FlagAttempt,
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
            db.GameplayFacts.Add(new GameplayFact
            {
                Id = Guid.CreateVersion7(),
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = fixture.FirstTeamId,
                ActorUserId = fixture.FirstUserId,
                Kind = GameplayFactKind.FlagAttempt,
                Value = value,
                ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(value)),
                State = GameplayFactState.Completed,
                Result = GameplayFactResult.Wrong,
                OccurredAt = fixture.Now.AddMinutes(index + 1),
                UpdatedAt = fixture.Now.AddMinutes(index + 1)
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static CompetitionEvent Event(
        Fixture fixture,
        Guid factId,
        CompetitionEventKind kind,
        DateTimeOffset occurredAt,
        GameplayFactResult result) => new()
    {
        Id = Guid.CreateVersion7(occurredAt),
        CompetitionId = fixture.CompetitionId,
        Kind = kind,
        Level = CompetitionEventLevel.Information,
        Visibility = CompetitionEventVisibility.Staff,
        SubjectType = EntityReferenceKind.GameplayFact,
        SubjectId = factId,
        RelatedType = EntityReferenceKind.Team,
        RelatedId = fixture.FirstTeamId,
        PayloadJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            gameplayFactState = GameplayFactState.Completed.ToString(),
            gameplayFactResult = result.ToString(),
            competitionChallengeId = fixture.CompetitionChallengeId
        }),
        OccurredAt = occurredAt
    };

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
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            ManagerIds = [managerId],
            JudgeIds = [judgeId],
            ObserverIds = [observerId],
            Title = "Adjudication preview",
            Mode = GameMode.Ctf,
            ConfigurationJson = "{}",
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
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = "Preview challenge",
            Direction = "Web",
            DefinitionJson = "{\"schemaVersion\":3,\"interactionKind\":0}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = "{}",
            UpdatedAt = now
        });
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = fixture.LaterFactId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = userId,
            Kind = GameplayFactKind.FlagAttempt,
            Value = "flag{preview}",
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{preview}")),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct,
            OccurredAt = now.AddSeconds(1),
            UpdatedAt = now.AddSeconds(1)
        });
        db.CompetitionEvents.AddRange(
            Event(fixture, fixture.LaterFactId, CompetitionEventKind.FirstBloodAwarded,
                now.AddSeconds(1), GameplayFactResult.Correct),
            Event(fixture, fixture.LaterFactId, CompetitionEventKind.GameplayFactAdjudicated,
                now.AddSeconds(1), GameplayFactResult.Correct));
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

    private sealed record AwdpFixture(
        Guid CompetitionId,
        Guid SameRoundLegacyDuplicateFactId,
        Guid CrossRoundLegacyDuplicateFactId,
        Guid CurrentCorrectFactId,
        Guid OtherDuplicateFactId);
}
