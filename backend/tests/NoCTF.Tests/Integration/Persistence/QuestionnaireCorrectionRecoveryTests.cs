using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Activities;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class QuestionnaireCorrectionRecoveryTests
{
    [Test, Timeout(300_000)]
    public async Task Preview_fences_and_late_correction_work_preserve_the_current_answer_and_future_papers(CancellationToken ct)
    {
        await using var fixture = await QuestionnaireApplicationFixture.CreateAsync(ct);
        var activity = await fixture.PublishAsync(QuestionnaireActivityKind.Examination, 600, 1, ct);
        var issued = await fixture.Sessions.IssueAsync(new(fixture.FirstUserId, activity, null, null, fixture.Now, Guid.NewGuid()), ct);
        var paper = issued.View!.Session!;
        var submitted = await fixture.Sessions.SubmitAsync(new(fixture.FirstUserId, activity, paper.Id, paper.AnswerRevision, fixture.Now.AddSeconds(1)), ct);
        await Assert.That(submitted.View!.Session!.Score).IsEqualTo(0L);
        var review = await fixture.Review.ReadAsync(fixture.OwnerId, activity, paper.Id, fixture.Now.AddSeconds(2), ct);
        var item = review!.Questions[0];
        var replacement = new QuestionnaireCorrectionInput(item.RevisionId, QuestionnaireCorrectionMode.ReplaceAnswer,
            [item.Question.Options.Single(value => value.Content == "incorrect").Id]);
        var preview = (await fixture.Review.PreviewAsync(fixture.OwnerId, activity, replacement, fixture.Now.AddSeconds(3), ct)).Preview!;
        var rejected = await fixture.Review.CorrectAsync(fixture.OwnerId, activity, replacement, Guid.NewGuid(), preview.ExpiresAt,
            "Invalid preview identity", fixture.Now.AddSeconds(4), ct);
        await Assert.That(rejected.Failure).IsEqualTo(QuestionnaireReviewFailure.PreviewChanged);
        await Assert.That(await fixture.Db.Set<QuestionnaireGradingOverride>().CountAsync(ct)).IsEqualTo(0);
        var first = await fixture.Review.CorrectAsync(fixture.OwnerId, activity, replacement, preview.Token, preview.ExpiresAt,
            "Correct the accepted answer.", fixture.Now.AddSeconds(5), ct);
        var fullCredit = new QuestionnaireCorrectionInput(item.RevisionId, QuestionnaireCorrectionMode.FullCredit, []);
        var latestPreview = (await fixture.Review.PreviewAsync(fixture.OwnerId, activity, fullCredit, fixture.Now.AddSeconds(6), ct)).Preview!;
        var latest = await fixture.Review.CorrectAsync(fixture.OwnerId, activity, fullCredit, latestPreview.Token, latestPreview.ExpiresAt,
            "Give full credit.", fixture.Now.AddSeconds(7), ct);
        await fixture.Review.ApplyAsync(new(activity, item.RevisionId, first.TargetRevision!.Value), fixture.Now.AddSeconds(8), ct);
        fixture.Db.ChangeTracker.Clear();
        await Assert.That((await fixture.Db.Set<QuestionnaireGradingOverride>().SingleAsync(ct)).AppliedRevision == latest.TargetRevision).IsFalse();
        var future = await fixture.Sessions.IssueAsync(new(fixture.SecondUserId, activity, null, null, fixture.Now.AddSeconds(9), Guid.NewGuid()), ct);
        var futurePaper = future.View!.Session!;
        var futureResult = await fixture.Sessions.SubmitAsync(new(fixture.SecondUserId, activity, futurePaper.Id, futurePaper.AnswerRevision, fixture.Now.AddSeconds(10)), ct);
        await Assert.That(futureResult.View!.Session!.Score).IsEqualTo(10L);
        await fixture.Review.ApplyAsync(new(activity, item.RevisionId, latest.TargetRevision!.Value), fixture.Now.AddSeconds(11), ct);
        fixture.Db.ChangeTracker.Clear();
        var corrected = await fixture.Sessions.ReadAsync(new(fixture.FirstUserId, activity, paper.Id, null, fixture.Now.AddSeconds(12)), ct);
        await Assert.That(corrected.View!.EffectiveScore.Points).IsEqualTo(10L);
        await Assert.That(await fixture.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(2);
    }
}
