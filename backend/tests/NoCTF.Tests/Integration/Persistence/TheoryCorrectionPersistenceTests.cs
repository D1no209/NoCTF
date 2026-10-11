using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Theory.Activities;
using NoCTF.Infrastructure.Theory.Papers;
using NoCTF.Tests.Fixtures.PostgreSql;
using NSubstitute;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TheoryCorrectionPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Corrections_are_preview_bound_recoverable_and_apply_to_future_papers_without_replacing_content(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options); await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var owner = User("correction-owner", UserRole.Organizer); var first = User("correction-first", UserRole.User); var second = User("correction-second", UserRole.User);
            db.Users.AddRange(owner, first, second);
            var source = new TheoryQuestionSource { Id = Guid.NewGuid() }; db.Add(source);
            var question = new TheoryQuestion { Id = Guid.NewGuid(), OwnerId = owner.Id, SourceId = source.Id, CurrentVersion = 1, State = TheoryQuestionState.Available };
            var original = new TheoryQuestionRevision { Id = Guid.NewGuid(), Version = 1, Title = "Synthetic correction", Body = "Immutable question", Kind = TheoryQuestionKind.SingleChoice,
                Options = [new() { Id = Guid.NewGuid(), Content = "a", IsCorrect = true }, new() { Id = Guid.NewGuid(), Content = "b", Order = 1 }] };
            question.Revisions.Add(original); db.TheoryQuestions.Add(question);
            var activity = new TheoryActivity { Id = Guid.NewGuid(), Name = "Correction exam", OwnerId = owner.Id, Kind = TheoryActivityKind.Examination,
                State = TheoryActivityState.Published, PublishedAt = now, OpensAt = now, ClosesAt = now.AddHours(1), DurationSeconds = 600,
                Rules = [new() { Id = Guid.NewGuid(), Count = 1, Points = 10, Candidates = [new() { RevisionId = original.Id, SourceId = source.Id }] }] };
            db.TheoryActivities.Add(activity); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            var papers = new TheoryPaperStore(db);
            var opened = await papers.IssueAsync(new(first.Id, activity.Id, null, null, now.AddSeconds(1), Guid.NewGuid()), ct);
            var item = opened.View!.Paper!.Questions.Single();
            var answered = await papers.AnswerAsync(new(first.Id, activity.Id, opened.View.Paper.Id, item.Id, item.AnswerRevision,
                Guid.NewGuid(), [item.Options.Single(option => option.Content == "b").Id], now.AddSeconds(2)), ct);
            var submitted = await papers.SubmitAsync(new(first.Id, activity.Id, opened.View.Paper.Id, answered.View!.Paper!.AnswerRevision, now.AddSeconds(3)), ct);
            await Assert.That(submitted.View!.Paper!.Score).IsEqualTo(0L);
            var publisher = Substitute.For<IPostCommitMessagePublisher>(); var leaderboard = Substitute.For<ILeaderboardCache>();
            var reviews = new TheoryReviewStore(db, publisher, leaderboard);
            var input = new TheoryCorrectionInput(original.Id, TheoryCorrectionMode.ReplaceAnswer, [original.Options[1].Id]);
            await Assert.That((await reviews.PreviewAsync(first.Id, activity.Id, input, now.AddSeconds(4), ct)).Failure).IsEqualTo(TheoryReviewFailure.NotFoundOrForbidden);
            var preview = (await reviews.PreviewAsync(owner.Id, activity.Id, input, now.AddSeconds(4), ct)).Preview!;
            await Assert.That(preview.Scores.Single().Before).IsEqualTo(0L);
            await Assert.That(preview.Scores.Single().After).IsEqualTo(10L);
            await Assert.That(preview.Participants.Single().After).IsEqualTo(10L);
            await Assert.That((await reviews.CorrectAsync(owner.Id, activity.Id, input, Guid.NewGuid(), preview.ExpiresAt, "Synthetic correction", now.AddSeconds(5), ct)).Failure)
                .IsEqualTo(TheoryReviewFailure.PreviewChanged);
            await using (var faultDb = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(new RejectCorrectionSave()).Options))
            {
                var faultReviews = new TheoryReviewStore(faultDb, publisher, leaderboard);
                await Assert.That(async () => await faultReviews.CorrectAsync(owner.Id, activity.Id, input, preview.Token, preview.ExpiresAt,
                    "Synthetic rollback", now.AddSeconds(5), ct)).Throws<DbUpdateException>();
            }
            await Assert.That(await db.Set<TheoryGradingOverride>().CountAsync(ct)).IsEqualTo(0);
            await publisher.DidNotReceive().PublishAsync(Arg.Any<ApplyTheoryCorrection>());
            var correction = await reviews.CorrectAsync(owner.Id, activity.Id, input, preview.Token, preview.ExpiresAt, "Synthetic correction", now.AddSeconds(6), ct);
            await Assert.That(correction.Failure).IsNull();
            var command = new ApplyTheoryCorrection(activity.Id, original.Id, correction.TargetRevision!.Value);
            await reviews.ApplyAsync(command, now.AddSeconds(7), ct);
            db.ChangeTracker.Clear();
            var fixedPaper = await db.TheoryPapers.Include(paper => paper.Items).SingleAsync(ct);
            await Assert.That(TheoryPaperGrading.Points(fixedPaper)).IsEqualTo(10L);
            await Assert.That(await db.TheoryQuestionRevisions.Where(version => version.Id == original.Id).Select(version => version.Body).SingleAsync(ct)).IsEqualTo("Immutable question");
            await Assert.That(await db.Set<TheoryQuestionOption>().Where(option => option.Id == original.Options[0].Id).Select(option => option.IsCorrect).SingleAsync(ct)).IsTrue();
            var fullCredit = new TheoryCorrectionInput(original.Id, TheoryCorrectionMode.FullCredit, []);
            var secondPreview = (await reviews.PreviewAsync(owner.Id, activity.Id, fullCredit, now.AddSeconds(8), ct)).Preview!;
            var newCorrection = await reviews.CorrectAsync(owner.Id, activity.Id, fullCredit, secondPreview.Token, secondPreview.ExpiresAt, "Synthetic full credit", now.AddSeconds(9), ct);
            await reviews.ApplyAsync(command, now.AddSeconds(10), ct); // Late old task must not consume the new target.
            await Assert.That((await db.Set<TheoryGradingOverride>().SingleAsync(ct)).AppliedRevision == newCorrection.TargetRevision).IsFalse();
            var future = await papers.IssueAsync(new(second.Id, activity.Id, null, null, now.AddSeconds(11), Guid.NewGuid()), ct);
            var futureSubmitted = await papers.SubmitAsync(new(second.Id, activity.Id, future.View!.Paper!.Id, future.View.Paper.AnswerRevision, now.AddSeconds(12)), ct);
            await Assert.That(futureSubmitted.View!.Paper!.Score).IsEqualTo(10L); // Full credit also covers unanswered future papers.
            await reviews.ApplyAsync(new(activity.Id, original.Id, newCorrection.TargetRevision!.Value), now.AddSeconds(13), ct);
            await Assert.That(await reviews.VoidPaperAsync(owner.Id, activity.Id, fixedPaper.Id, "Synthetic void", now.AddSeconds(14), ct)).IsNull();
            await Assert.That(await db.Set<TheoryDrawReceipt>().CountAsync(ct)).IsEqualTo(2);
            await Assert.That(await db.TheoryParticipants.Where(participant => participant.UserId == first.Id).Select(participant => participant.IssuedCount).SingleAsync(ct)).IsEqualTo(1);
            var board = await reviews.LeaderboardAsync(second.Id, activity.Id, 0, 20, now.AddSeconds(15), ct);
            await Assert.That(board!.Items.First().UserId).IsEqualTo(second.Id);
            await Assert.That(board.Items.Single(row => row.UserId == first.Id).Points).IsEqualTo(0L);
        });
    }

    private static User User(string name, UserRole role) => new() { Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(),
        Email = name + "@example.invalid", NormalizedEmail = (name + "@example.invalid").ToUpperInvariant(), PasswordHash = "not-a-login", Role = role };
    private sealed class RejectCorrectionSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<TheoryGradingOverride>().Any(entry => entry.State == EntityState.Added))
                throw new DbUpdateException("Synthetic correction rollback.");
            return ValueTask.FromResult(result);
        }
    }
}
