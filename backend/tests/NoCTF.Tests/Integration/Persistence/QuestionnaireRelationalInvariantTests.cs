using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class QuestionnaireRelationalInvariantTests
{
    [Test, Timeout(300_000)]
    public async Task Issued_content_rejects_in_place_question_option_and_source_changes(CancellationToken ct)
    {
        await using var fixture = await QuestionnaireApplicationFixture.CreateAsync(ct);
        var activity = await fixture.PublishAsync(QuestionnaireActivityKind.Practice, 600, 2, ct);
        var issued = await fixture.Sessions.IssueAsync(new(fixture.FirstUserId, activity, null, null, fixture.Now, Guid.NewGuid()), ct);
        await Assert.That(issued.Failure).IsNull();
        var db = fixture.Db; db.ChangeTracker.Clear();
        var question = await db.Set<QuestionnaireQuestion>().FirstAsync(ct);
        question.Body = "Updated content requires a new version.";
        await Assert.That(() => db.SaveChangesAsync(ct)).Throws<InvalidOperationException>(); db.ChangeTracker.Clear();
        var option = await db.Set<QuestionnaireOption>().FirstAsync(ct); option.IsCorrect = !option.IsCorrect;
        await Assert.That(() => db.SaveChangesAsync(ct)).Throws<InvalidOperationException>(); db.ChangeTracker.Clear();
        question = await db.Set<QuestionnaireQuestion>().FirstAsync(ct);
        db.Set<QuestionnaireOption>().Add(new() { Id = Guid.NewGuid(), RevisionId = question.RevisionId,
            QuestionVersionId = question.VersionId, QuestionId = question.Id, Position = 2, Content = "Late option" });
        await Assert.That(() => db.SaveChangesAsync(ct)).Throws<InvalidOperationException>(); db.ChangeTracker.Clear();
        question = await db.Set<QuestionnaireQuestion>().FirstAsync(ct); question.SourceId = Guid.NewGuid();
        await Assert.That(() => db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
    }

    [Test, Timeout(300_000)]
    public async Task One_assignment_has_one_formal_fact_and_paper_updates_use_real_optimistic_concurrency(CancellationToken ct)
    {
        await using var fixture = await QuestionnaireApplicationFixture.CreateAsync(ct);
        var activity = await fixture.PublishAsync(QuestionnaireActivityKind.Practice, 600, 2, ct);
        var issued = await fixture.Sessions.IssueAsync(new(fixture.FirstUserId, activity, null, null, fixture.Now, Guid.NewGuid()), ct);
        var paper = issued.View!.Session!;
        var submitted = await fixture.Sessions.SubmitAsync(new(fixture.FirstUserId, activity, paper.Id, paper.AnswerRevision, fixture.Now.AddSeconds(1)), ct);
        await Assert.That(submitted.Failure).IsNull(); fixture.Db.ChangeTracker.Clear();
        var fact = await fixture.Db.GameplayFacts.AsSplitQuery().SingleAsync(ct);
        fixture.Db.GameplayFacts.Add(new QuestionnaireAttemptGameplayFact { Id = Guid.NewGuid(), ActorUserId = fixture.FirstUserId,
            QuestionnaireAssignmentId = fact.QuestionnaireAssignmentId, State = GameplayFactState.Completed, Result = GameplayFactResult.Wrong,
            OccurredAt = fixture.Now.AddSeconds(2), UpdatedAt = fixture.Now.AddSeconds(2) });
        await Assert.That(() => fixture.Db.SaveChangesAsync(ct)).Throws<DbUpdateException>(); fixture.Db.ChangeTracker.Clear();
        await Assert.That(await fixture.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(1);
        await using var parallel = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(fixture.Db.Database.GetConnectionString()).Options);
        var first = await fixture.Db.QuestionnaireSessions.SingleAsync(value => value.Id == paper.Id, ct);
        var second = await parallel.QuestionnaireSessions.SingleAsync(value => value.Id == paper.Id, ct);
        first.SubmittedById = fixture.OwnerId; second.SubmittedById = fixture.SecondUserId;
        await fixture.Db.SaveChangesAsync(ct);
        await Assert.That(() => parallel.SaveChangesAsync(ct)).Throws<DbUpdateConcurrencyException>();
    }
}
