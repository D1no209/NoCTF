using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloSelectionAndPreparationTests
{
    [Test, Timeout(300_000)]
    public async Task Unpublished_static_questions_can_be_prepared_before_the_competition_without_isolation_or_media(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            f.Competition.Status = CompetitionStatus.Published;
            foreach (var entry in f.Entries) entry.IsPublished = false;
            await f.Db.SaveChangesAsync(ct); await f.PrepareAsync(ct);
            var store = f.Store(f.Db);
            var round = await store.FindRoundAsync(f.Competition.Id, f.Match.Id, f.Round.Id, f.Owner.Id, true, f.Now, ct);
            await Assert.That(round!.ResourceReadiness).IsEqualTo(LiveSoloQuestionReadiness.Ready);
            await Assert.That(round.EnvironmentRequired).IsFalse();
            await Assert.That(round.PlatformStreamingEnabled).IsFalse();
            var start = await store.StartCountdownAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, f.Owner.Id, round.ConcurrencyStamp, f.Now), ct);
            await Assert.That(start.Failure).IsEqualTo(LiveSoloFailure.CompetitionNotStarted);
            f.Competition.Status = CompetitionStatus.Running; await f.Db.SaveChangesAsync(ct); await f.StartAsync(ct);
            await Assert.That((await store.QuestionsAsync(f.Competition.Id, f.Match.Id, f.Round.Id, f.Left.Id, f.Now, ct))!.Count).IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Same_source_is_a_reuse_risk_for_explicit_selection_and_is_excluded_from_automatic_selection(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            f.Db.LiveSoloChallengeSources.Add(new() { ChallengeId = f.Templates[1].Id, OriginalChallengeId = f.Templates[0].Id, CanonicalChallengeId = f.Templates[0].Id, CopiedAt = f.Now });
            await f.Db.SaveChangesAsync(ct); await f.PrepareAsync(ct);
            var round = await f.Db.LiveSoloRounds.Include(x => x.SelectionRisks).Include(x => x.SelectionAudits).SingleAsync(ct);
            await Assert.That(round.SelectionKind).IsEqualTo(LiveSoloQuestionSelectionKind.StaffSpecified);
            await Assert.That(round.SelectionRisks.Any(x => x.Kind == LiveSoloSelectionRiskKind.SameSource)).IsTrue();
            await Assert.That(round.SelectionAudits.Single().ActorUserId).IsEqualTo(f.Owner.Id);
            var candidates = (await f.Store(f.Db).CandidatesAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, f.Now, ct))!;
            await Assert.That(candidates.Single().Available).IsTrue();
            await Assert.That(candidates.Single().AutomaticEligible).IsFalse();
            await Assert.That(LiveSoloQuestionSelection.Select(candidates, null)).IsNull();
            await Assert.That(LiveSoloQuestionSelection.Select(candidates, candidates.Single().Id)).IsNotNull();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Acquisition_policy_is_off_by_default_and_is_fixed_for_the_prepared_round(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            await f.PrepareAsync(ct);
            await Assert.That(f.Round.StaticFlagEvidenceRequired).IsFalse();
            ((LiveSoloCompetitionModeConfiguration)f.Competition.ModeConfiguration!).StaticFlagEvidenceRequired = true;
            await f.Db.SaveChangesAsync(ct); await f.StartAsync(ct);
            var question = f.Round.Questions.Single(x => x.Position == 0);
            var accepted = await f.Store(f.Db).AdmitAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, question.Id, f.Left.Id, "flag{first}", f.Now), ct);
            var fact = await f.Db.GameplayFacts.Include(x => x.AcquisitionEvidence).SingleAsync(x => x.Id == accepted.GameplayFactId, ct);
            await Assert.That(fact.AcquisitionEvidence!.Scope).IsEqualTo(NoCTF.Domain.Gameplay.FlagAcquisitionScope.NotApplicable);
            await f.Store(f.Db).ResolveAsync(f.Round.Id, f.Now, ct);
            await Assert.That(fact.Result).IsEqualTo(NoCTF.Domain.Gameplay.GameplayFactResult.Correct);
            await Assert.That(f.Round.StaticFlagEvidenceRequired).IsFalse();
        });
    }
    [Test, Timeout(300_000)]
    public async Task Ordinary_publication_paid_hints_and_manual_adjustment_are_rejected_for_solo(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var messages = NSubstitute.Substitute.For<NoCTF.Application.Messaging.IPostCommitMessagePublisher>();
            var manager = new NoCTF.Infrastructure.Challenges.Management.ChallengeManagementStore(f.Db, messages, new NoCTF.GameModes.Registration.ChallengeRuntimeTemplateCatalog());
            var published = await manager.UpdateAsync(new(f.Competition.Id, f.Entries[0].Id, f.Entries[0].Order, true, f.Now), ct);
            await Assert.That(published.Failure).IsEqualTo(NoCTF.Application.Challenges.Management.ChallengeMutationFailure.PublicationUnsupported);
            var tags = await manager.UpdateAsync(new(f.Competition.Id, f.Entries[0].Id, f.Entries[0].Order, f.Entries[0].IsPublished, f.Now, Tags: ["Web"], HasPublicationChange: false), ct);
            await Assert.That(tags.Failure).IsNull();
            await Assert.That((await f.Db.CompetitionChallenges.SingleAsync(x => x.Id == f.Entries[0].Id, ct)).ChallengeId).IsEqualTo(f.Templates[0].Id);
            var hints = new NoCTF.Infrastructure.Challenges.Hints.ChallengeHintStore(f.Db, messages);
            var paid = await hints.SaveAsync(new(f.Competition.Id, f.Entries[0].Id, null, true, "Synthetic hint", 10, f.Now, f.Now), ct);
            await Assert.That(paid.Failure).IsEqualTo(NoCTF.Application.Challenges.Hints.ChallengeHintSaveFailure.PaidHintUnsupported);
            var intake = new NoCTF.Infrastructure.GameplayFacts.Intake.GameplayFactIntakeStore(f.Db, messages);
            var adjustment = await intake.TryAcceptManualAdjustmentAsync(new(Guid.NewGuid(), f.Competition.Id, f.LeftTeam.Id, f.Entries[0].Id, f.Owner.Id, 10, f.Now), ct);
            await Assert.That(adjustment.State).IsEqualTo(NoCTF.Application.GameplayFacts.Intake.GameplayFactAcceptanceState.UnsupportedMode);
            await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Cancellation_before_opening_releases_the_list_and_explicit_continuation_appends_a_risk_audit(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false); await f.PrepareAsync(ct);
            var store = f.Store(f.Db); var oldRound = f.Round;
            var canceled = await store.ApplyAsync(new NoCTF.Application.LiveSolo.Adjudication.AdjudicateLiveSoloMatch(f.Competition.Id, f.Match.Id,
                f.Owner.Id, f.Match.ConcurrencyStamp, oldRound.Id, oldRound.ConcurrencyStamp, oldRound.TimelineRevision,
                LiveSoloJudgeAction.VoidRound, null, "Synthetic canceled preparation"), ct);
            await Assert.That(canceled.Failure).IsNull();
            var candidates = (await store.CandidatesAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, f.Now, ct))!;
            await Assert.That(candidates.Single().AutomaticEligible).IsTrue();
            var prepared = await store.PrepareRoundAsync(new(f.Competition.Id, f.Match.Id, f.Owner.Id, canceled.Match!.ConcurrencyStamp, null, f.Now), ct);
            await Assert.That(prepared.Failure).IsNull(); await Assert.That(prepared.Round!.Id).IsNotEqualTo(oldRound.Id);
            await Assert.That(prepared.Round.QuestionGroupId).IsEqualTo(oldRound.QuestionGroupId);
            f.Db.LiveSoloQuestionExposures.Add(new() { CompetitionId = f.Competition.Id, CanonicalChallengeId = f.Templates[0].Id, MatchId = f.Match.Id, PublicAt = f.Now });
            await f.Db.SaveChangesAsync(ct);
            var selected = (await store.CandidatesAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, f.Now, ct))!.Single();
            await Assert.That(selected.Risks.Any(x => x.Kind == LiveSoloSelectionRiskKind.PublicExposure)).IsTrue();
            var result = await store.SpecifyPreparedGroupAsync(new(f.Competition.Id, f.Match.Id, prepared.Round.Id, selected.Id, f.Owner.Id,
                prepared.Round.ConcurrencyStamp, selected.ConcurrencyStamp, selected.SelectionRevision, f.Now), ct);
            await Assert.That(result.Failure).IsNull();
            var round = await f.Db.LiveSoloRounds.Include(x => x.SelectionAudits).Include(x => x.SelectionRisks).SingleAsync(x => x.Id == prepared.Round.Id, ct);
            await Assert.That(round.SelectionAudits.Count).IsEqualTo(2);
            await Assert.That(round.SelectionAudits.OrderBy(x => x.Position).First().Kind).IsEqualTo(LiveSoloQuestionSelectionKind.Automatic);
            await Assert.That(round.SelectionKind).IsEqualTo(LiveSoloQuestionSelectionKind.StaffSpecified);
            await Assert.That(round.Questions.All(x => x.OpenedAt == null)).IsTrue();
            await Assert.That(round.SelectionRisks.Single().Kind).IsEqualTo(LiveSoloSelectionRiskKind.PublicExposure);
        });
    }

}
