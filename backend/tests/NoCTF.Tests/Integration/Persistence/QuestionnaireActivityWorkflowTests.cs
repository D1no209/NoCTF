using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class QuestionnaireActivityWorkflowTests
{
    [Test, Timeout(300_000)]
    public async Task Independent_sessions_isolate_users_and_respect_pause_timeout_and_activity_finish(CancellationToken ct)
    {
        await using var fixture = await QuestionnaireApplicationFixture.CreateAsync(ct);
        var activityId = await fixture.PublishAsync(QuestionnaireActivityKind.Examination, 10, 1, ct);
        var issued = await fixture.Sessions.IssueAsync(new(fixture.FirstUserId, activityId, null, null, fixture.Now.AddSeconds(1), Guid.NewGuid()), ct);
        await Assert.That(issued.Failure).IsNull(); var session = issued.View!.Session!;
        await Assert.That((await fixture.Sessions.ReadAsync(new(fixture.SecondUserId, activityId, session.Id, null, fixture.Now.AddSeconds(2)), ct)).Failure)
            .IsEqualTo(QuestionnaireSessionFailure.NotFoundOrForbidden);
        var question = session.Questions[0]; var requestId = Guid.NewGuid();
        var answer = new QuestionnaireAnswerCommand(fixture.FirstUserId, activityId, session.Id, question.Id, question.AnswerRevision, requestId,
            [question.Options.Single(value => value.Content == "correct").Id], fixture.Now.AddSeconds(2));
        await Assert.That((await fixture.Sessions.AnswerAsync(answer, ct)).Failure).IsNull();
        await Assert.That((await fixture.Sessions.AnswerAsync(answer, ct)).Failure).IsNull();
        await Assert.That((await fixture.Activities.TransitionAsync(new(fixture.OwnerId, activityId, QuestionnaireActivityAction.Pause, fixture.Now.AddSeconds(3)), ct)).Failure).IsNull();
        await Assert.That((await fixture.Sessions.AnswerAsync(answer with { RequestId = Guid.NewGuid(), Now = fixture.Now.AddSeconds(9) }, ct)).Failure)
            .IsEqualTo(QuestionnaireSessionFailure.Paused);
        await Assert.That((await fixture.Activities.TransitionAsync(new(fixture.OwnerId, activityId, QuestionnaireActivityAction.Resume, fixture.Now.AddSeconds(13)), ct)).Failure).IsNull();
        var active = await fixture.Sessions.ReadAsync(new(fixture.FirstUserId, activityId, session.Id, null, fixture.Now.AddSeconds(15)), ct);
        await Assert.That(active.View!.Session!.State).IsEqualTo(QuestionnaireSessionState.Active);
        var second = await fixture.Sessions.IssueAsync(new(fixture.SecondUserId, activityId, null, null, fixture.Now.AddSeconds(18), Guid.NewGuid()), ct);
        await Assert.That(second.Failure).IsNull(); await Assert.That(second.View!.Session!.Id).IsNotEqualTo(session.Id);
        var expired = await fixture.Sessions.ReadAsync(new(fixture.FirstUserId, activityId, session.Id, null, fixture.Now.AddSeconds(22)), ct);
        await Assert.That(expired.Failure).IsNull();
        await Assert.That(expired.View!.Session!.State).IsEqualTo(QuestionnaireSessionState.Submitted);
        await Assert.That(expired.View.Session.SubmissionKind).IsEqualTo(QuestionnaireSubmissionKind.Timeout);
        await Assert.That(expired.View.Session.Score).IsEqualTo(10L);
        await Assert.That((await fixture.Activities.TransitionAsync(new(fixture.OwnerId, activityId, QuestionnaireActivityAction.Finish,
            fixture.Now.AddSeconds(24), Reason: "Finish the examination."), ct)).Failure).IsNull();
        await fixture.Sessions.SweepAsync(activityId, fixture.Now.AddSeconds(25), ct);
        fixture.Db.ChangeTracker.Clear();
        var finished = await fixture.Sessions.ReadAsync(new(fixture.SecondUserId, activityId, second.View.Session.Id, null, fixture.Now.AddSeconds(26)), ct);
        await Assert.That(finished.View!.Session!.SubmissionKind).IsEqualTo(QuestionnaireSubmissionKind.ActivityFinished);
        await Assert.That(finished.View.Session.Score).IsEqualTo(0L);
        await Assert.That(await fixture.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(2);
        await Assert.That(await fixture.Db.GameplayFacts.AnyAsync(value => value.CompetitionId != null || value.CompetitionChallengeId != null || value.TeamId != null, ct)).IsFalse();
        await Assert.That(await fixture.Db.Set<QuestionnaireActiveSession>().CountAsync(ct)).IsEqualTo(0);
    }

    [Test, Timeout(300_000)]
    public async Task Corrections_update_current_question_results_scores_and_audit_without_additional_facts(CancellationToken ct)
    {
        await using var fixture = await QuestionnaireApplicationFixture.CreateAsync(ct);
        var activityId = await fixture.PublishAsync(QuestionnaireActivityKind.Practice, 600, 2, ct);
        var issued = await fixture.Sessions.IssueAsync(new(fixture.FirstUserId, activityId, null, null, fixture.Now.AddSeconds(1), Guid.NewGuid()), ct);
        var session = issued.View!.Session!; var question = session.Questions[0];
        var wrong = question.Options.Single(value => value.Content == "incorrect").Id;
        var answered = await fixture.Sessions.AnswerAsync(new(fixture.FirstUserId, activityId, session.Id, question.Id, question.AnswerRevision,
            Guid.NewGuid(), [wrong], fixture.Now.AddSeconds(2)), ct);
        var submitted = await fixture.Sessions.SubmitAsync(new(fixture.FirstUserId, activityId, session.Id, answered.View!.Session!.AnswerRevision, fixture.Now.AddSeconds(3)), ct);
        await Assert.That(submitted.View!.Session!.Score).IsEqualTo(0L);
        var review = await fixture.Review.ReadAsync(fixture.OwnerId, activityId, session.Id, fixture.Now.AddSeconds(4), ct);
        await Assert.That(review!.Questions.Count).IsEqualTo(2);
        await Assert.That(await fixture.Review.ReadAsync(fixture.SecondUserId, activityId, session.Id, fixture.Now.AddSeconds(4), ct)).IsNull();
        var revisionId = review.Questions.Single(value => value.Question.Id == question.Id).RevisionId;
        var correctionOffset = 5;
        foreach (var mode in new[] { QuestionnaireCorrectionMode.ReplaceAnswer, QuestionnaireCorrectionMode.FullCredit })
        {
            var correction = new QuestionnaireCorrectionInput(revisionId, mode, mode == QuestionnaireCorrectionMode.ReplaceAnswer ? [wrong] : []);
            var preview = await fixture.Review.PreviewAsync(fixture.OwnerId, activityId, correction, fixture.Now.AddSeconds(correctionOffset), ct);
            await Assert.That(preview.Failure).IsNull();
            var committed = await fixture.Review.CorrectAsync(fixture.OwnerId, activityId, correction, preview.Preview!.Token,
                preview.Preview.ExpiresAt, "Correct the question.", fixture.Now.AddSeconds(correctionOffset + 1), ct);
            await Assert.That(committed.Failure).IsNull();
            await fixture.Review.ApplyAsync(new(activityId, revisionId, committed.TargetRevision!.Value), fixture.Now.AddSeconds(correctionOffset + 2), ct);
            fixture.Db.ChangeTracker.Clear();
            var current = await fixture.Sessions.ReadAsync(new(fixture.FirstUserId, activityId, session.Id, null, fixture.Now.AddSeconds(correctionOffset + 3)), ct);
            await Assert.That(current.View!.EffectiveScore.Points).IsEqualTo(10L);
            var fact = await fixture.Db.GameplayFacts.AsSplitQuery().SingleAsync(ct);
            await Assert.That(fact.CapabilityOutcomes.OfType<QuestionnaireOutcome>().Single().Questions.Count).IsEqualTo(2);
            await Assert.That(await fixture.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(1);
            correctionOffset += 10;
        }
        await Assert.That(await fixture.Review.VoidSessionAsync(fixture.OwnerId, activityId, session.Id, "Withdraw the session.", fixture.Now.AddSeconds(25), ct)).IsNull();
        fixture.Db.ChangeTracker.Clear();
        var voided = await fixture.Sessions.ReadAsync(new(fixture.FirstUserId, activityId, session.Id, null, fixture.Now.AddSeconds(26)), ct);
        await Assert.That(voided.View!.EffectiveScore.Points).IsEqualTo(0L);
        await Assert.That((await fixture.Db.GameplayFacts.AsSplitQuery().SingleAsync(ct)).Result).IsEqualTo(GameplayFactResult.Rejected);
        var audit = await fixture.Review.AuditAsync(fixture.OwnerId, activityId, 0, 100, fixture.Now.AddSeconds(26), ct);
        await Assert.That(audit!.Items.Any(value => value.Kind == QuestionnaireActivityOperationKind.AnswerCorrected)).IsTrue();
        await Assert.That(audit.Items.Any(value => value.Kind == QuestionnaireActivityOperationKind.FullCreditGranted)).IsTrue();
        await Assert.That(audit.Items.Any(value => value.Kind == QuestionnaireActivityOperationKind.SessionVoided)).IsTrue();
    }
}
