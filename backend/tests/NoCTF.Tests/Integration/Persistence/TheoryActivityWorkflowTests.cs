using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Theory.Activities;
using NoCTF.Infrastructure.Theory.Papers;
using NoCTF.Infrastructure.Theory.Questions;
using NoCTF.Tests.Fixtures.PostgreSql;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Theory.Scoring;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TheoryActivityWorkflowTests
{
    [Test, Timeout(300_000)]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    public async Task Team_members_share_answers_and_formal_scores_respect_cutoff_and_scope(GameMode mode, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options); await db.Database.EnsureCreatedAsync(ct);
            var owner = User("formal-owner", UserRole.Organizer); var captain = User("formal-captain", UserRole.User);
            var member = User("formal-member", UserRole.User); var outsider = User("formal-outsider", UserRole.User);
            db.Users.AddRange(owner, captain, member, outsider);
            var now = DateTimeOffset.UtcNow;
            var competition = CompetitionGeneratedCatalog.Create(mode);
            competition.Id = Guid.NewGuid(); competition.OwnerId = owner.Id; competition.Title = "Theory formal test";
            competition.FlagDerivationSecret = new byte[32]; competition.ModeConfiguration = TestConfigurations.Competition(mode);
            competition.Status = CompetitionStatus.Running; competition.StartAt = now.AddMinutes(-10); competition.EndAt = now.AddHours(2);
            db.Competitions.Add(competition);
            var team = new Team { Id = Guid.NewGuid(), CompetitionId = competition.Id, Name = "Theory team", CaptainId = captain.Id,
                MemberIds = [captain.Id, member.Id], InvitationToken = new string('q', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = now };
            db.Teams.Add(team); await db.SaveChangesAsync(ct);
            var questions = new ManageTheoryQuestions(new TheoryQuestionBank(db));
            var question = await questions.SaveAsync(new(owner.Id, null, new("Formal question", "Choose", null,
                TheoryQuestionKind.SingleChoice, TheoryQuestionDifficulty.Basic, [new("right", true), new("wrong", false)], [], []), now), ct);
            await questions.SetStateAsync(owner.Id, question.Saved!.QuestionId, TheoryQuestionState.Available, now, ct);
            var activities = new TheoryActivityStore(db);
            var saved = await activities.SaveAsync(new(owner.Id, null, competition.Id, new("Formal theory", TheoryActivityKind.Competition,
                now, now.AddHours(1), 60, 1, TheoryScoringMode.Highest, false, [new(TheoryQuestionKind.SingleChoice, null, 1, 20, [])]), now), ct);
            await Assert.That(saved.Failure).IsNull();
            var activityId = saved.Activity!.Id;
            await activities.TransitionAsync(new(owner.Id, activityId, TheoryActivityAction.Publish, now), ct);
            var sourceIds = await db.Set<TheoryQuestionSource>().Select(source => source.Id).ToArrayAsync(ct);
            await Assert.That((await TheoryReservations.ReadAsync(db, sourceIds, Guid.NewGuid(), now.AddSeconds(1), ct)).Count).IsEqualTo(1);
            await Assert.That((await TheoryReservations.ReadAsync(db, sourceIds, Guid.NewGuid(), now.AddHours(4), ct)).Count).IsEqualTo(0);
            var store = new TheoryPaperStore(db);
            var first = await store.IssueAsync(new(captain.Id, activityId, null, null, now.AddSeconds(1), Guid.NewGuid()), ct);
            await Assert.That(first.Failure).IsNull();
            var shared = await store.ReadAsync(new(member.Id, activityId, null, null, now.AddSeconds(2)), ct);
            await Assert.That(shared.View!.Paper!.Id).IsEqualTo(first.View!.Paper!.Id);
            var item = shared.View.Paper.Questions.Single();
            var answered = await store.AnswerAsync(new(member.Id, activityId, shared.View.Paper.Id, item.Id, item.AnswerRevision,
                Guid.NewGuid(), [item.Options.Single(option => option.Content == "right").Id], now.AddSeconds(3)), ct);
            await Assert.That(answered.Failure).IsNull();
            await activities.TransitionAsync(new(owner.Id, activityId, TheoryActivityAction.Pause, now.AddSeconds(5)), ct);
            await Assert.That((await store.SubmitAsync(new(captain.Id, activityId, shared.View.Paper.Id, answered.View!.Paper!.AnswerRevision, now.AddSeconds(10)), ct)).Failure)
                .IsEqualTo(TheoryPaperFailure.Paused);
            await activities.TransitionAsync(new(owner.Id, activityId, TheoryActivityAction.Resume, now.AddSeconds(15)), ct);
            var result = await store.SubmitAsync(new(member.Id, activityId, shared.View.Paper.Id, answered.View.Paper.AnswerRevision, now.AddSeconds(20)), ct);
            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.View!.Paper!.Score).IsEqualTo(20L);
            await Assert.That(result.View.Deadline.At).IsEqualTo(now.AddSeconds(71));
            var fact = await db.GameplayFacts.SingleAsync(ct);
            await Assert.That(fact.Kind).IsEqualTo(GameplayFactKind.TheoryPaperSubmission);
            await Assert.That(fact.CompetitionChallengeId).IsNull();
            await Assert.That(fact.TheoryPaperId).IsEqualTo(result.View.Paper.Id);
            await Assert.That(fact.TeamId).IsEqualTo(team.Id);
            await Assert.That((await TheoryScoreReader.ReadAsync(db, competition.Id, now.AddSeconds(19), ct)).Scores!.Single().Points).IsEqualTo(0L);
            await Assert.That((await TheoryScoreReader.ReadAsync(db, competition.Id, now.AddSeconds(21), ct)).Scores!.Single().Points).IsEqualTo(20L);
            await Assert.That((await store.ReadAsync(new(outsider.Id, activityId, result.View.Paper.Id, null, now.AddSeconds(21)), ct)).Failure)
                .IsEqualTo(TheoryPaperFailure.NotFoundOrForbidden);
            await Assert.That(await db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(answered.View.Paper.Questions.Single().AnsweredByName).IsEqualTo(member.UserName);
            var audience = new NoCTF.Infrastructure.Theory.Realtime.TheoryChangeAudienceResolver(new ContextFactory(options));
            var participant = await db.TheoryParticipants.SingleAsync(ct);
            var recipients = await audience.ResolveAsync(new(activityId, participant.Id), ct);
            await Assert.That(recipients.Contains(member.Id)).IsTrue();
            await Assert.That(recipients.Contains(outsider.Id)).IsFalse();
            var persistedTeam = await db.Teams.SingleAsync(item => item.Id == team.Id, ct);
            persistedTeam.IsBanned = true;
            await db.SaveChangesAsync(ct);
            recipients = await audience.ResolveAsync(new(activityId, participant.Id), ct);
            await Assert.That(recipients.Contains(member.Id)).IsFalse();
            await Assert.That(recipients.Contains(owner.Id)).IsTrue();
            var review = new TheoryReviewStore(db, NSubstitute.Substitute.For<NoCTF.Application.Messaging.IPostCommitMessagePublisher>(),
                NSubstitute.Substitute.For<NoCTF.Application.Scoring.Leaderboard.ILeaderboardCache>());
            var monitor = await review.ListAsync(owner.Id, activityId, 0, 20, now.AddSeconds(21), ct);
            await Assert.That(monitor!.Items.Single().AnsweredCount).IsEqualTo(1);
            await Assert.That(monitor.Items.Single().EffectiveScore!.Points).IsEqualTo(20L);
            await Assert.That((await review.AuditAsync(owner.Id, activityId, 0, 20, now.AddSeconds(21), ct))!.Total).IsEqualTo(3);
            await Assert.That(await review.AuditAsync(outsider.Id, activityId, 0, 20, now.AddSeconds(21), ct)).IsNull();
            await Assert.That(await review.StartConsultationAsync(outsider.Id, activityId, result.View.Paper.Id, "Private synthetic consultation", now.AddSeconds(22), ct)).IsNull();
            var thread = await review.StartConsultationAsync(owner.Id, activityId, result.View.Paper.Id, "Private synthetic consultation", now.AddSeconds(22), ct);
            await Assert.That(thread).IsNotNull();
            await Assert.That(await review.StartConsultationAsync(owner.Id, activityId, result.View.Paper.Id, "Private synthetic consultation", now.AddSeconds(23), ct)).IsEqualTo(thread);
            await db.Notifications.Where(item => item.Id == thread).ExecuteDeleteAsync(ct);
            var snapshot = await TheoryScoreReader.ReadAsync(db, competition.Id, now.AddSeconds(21), ct);
            await Assert.That(snapshot.Receipts.Single().ConcurrencyStamp).IsEqualTo(fact.ConcurrencyStamp);
            // The deletion graph must be atomic and must never delete reusable bank content.
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await db.GameplayFacts.Where(item => item.CompetitionId == competition.Id).ExecuteDeleteAsync(ct);
                await TheoryCompetitionDeletion.DeleteAsync(db, competition.Id, ct);
                await Assert.That(await db.TheoryPapers.AnyAsync(ct)).IsFalse();
                await transaction.RollbackAsync(ct);
            }
            db.ChangeTracker.Clear();
            await Assert.That(await db.TheoryPapers.CountAsync(ct)).IsEqualTo(1);
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await db.GameplayFacts.Where(item => item.CompetitionId == competition.Id).ExecuteDeleteAsync(ct);
                await TheoryCompetitionDeletion.DeleteAsync(db, competition.Id, ct);
                await transaction.CommitAsync(ct);
            }
            await Assert.That(await db.TheoryQuestions.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await db.TheoryPapers.AnyAsync(ct)).IsFalse();
            await Assert.That((await db.Set<TheoryQuestionSource>().SingleAsync(ct)).FirstExposedAt).IsNotNull();

        });
    }

    [Test, Timeout(300_000)]
    public async Task Concurrent_issue_replay_and_submission_keep_one_paper_and_never_repeat_sources(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options); await db.Database.EnsureCreatedAsync(ct);
            var owner = User("workflow-owner", UserRole.Organizer); var actor = User("workflow-actor", UserRole.User);
            var other = User("workflow-other", UserRole.User); db.Users.AddRange(owner, actor, other); await db.SaveChangesAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var bank = new TheoryQuestionBank(db); var manager = new ManageTheoryQuestions(bank);
            for (var index = 0; index < 6; index++)
            {
                var question = await manager.SaveAsync(new(owner.Id, null, new($"Question {index}", $"Body {index}", "PRIVATE",
                    TheoryQuestionKind.SingleChoice, TheoryQuestionDifficulty.Basic, [new("right", true), new("wrong", false)], [], []), now), ct);
                await manager.SetStateAsync(owner.Id, question.Saved!.QuestionId, TheoryQuestionState.Available, now, ct);
            }
            var activities = new TheoryActivityStore(db);
            var config = new TheoryActivityInput("Synthetic exam", TheoryActivityKind.Examination, now, now.AddHours(2),
                1800, 2, TheoryScoringMode.Highest, false, [new(TheoryQuestionKind.SingleChoice, null, 2, 10, [])]);
            var saved = await activities.SaveAsync(new(owner.Id, null, null, config, now), ct);
            await Assert.That(saved.Failure).IsNull();
            var activityId = saved.Activity!.Id;
            var published = await activities.TransitionAsync(new(owner.Id, activityId, TheoryActivityAction.Publish, now), ct);
            await Assert.That(published.Failure).IsNull();
            await activities.TransitionAsync(new(owner.Id, activityId, TheoryActivityAction.Pause, now.AddMilliseconds(100)), ct);
            await activities.TransitionAsync(new(owner.Id, activityId, TheoryActivityAction.StopIssuance, now.AddMilliseconds(200)), ct);
            await activities.TransitionAsync(new(owner.Id, activityId, TheoryActivityAction.Withdraw, now.AddMilliseconds(300)), ct);
            var republished = await activities.TransitionAsync(new(owner.Id, activityId, TheoryActivityAction.Publish, now.AddMilliseconds(400)), ct);
            await Assert.That(republished.Failure).IsNull();
            await Assert.That(republished.Activity!.IssuanceStopped).IsFalse();
            db.ChangeTracker.Clear();
            var commands = Enumerable.Range(0, 4).Select(_ => new TheoryPaperCommand(actor.Id, activityId, null, null,
                now.AddSeconds(1), Guid.NewGuid())).ToArray();
            var opened = await Task.WhenAll(commands.Select(async command =>
            {
                await using var concurrentDb = new NoCtfDbContext(options);
                return await new TheoryPaperStore(concurrentDb).IssueAsync(command, ct);
            }));
            await Assert.That(string.Join(",", opened.Where(result => result.Failure is not null).Select(result => result.Failure))).IsEqualTo(string.Empty);
            await Assert.That(opened.Select(result => result.View!.Paper!.Id).Distinct().Count()).IsEqualTo(1);
            await Assert.That(await db.TheoryPapers.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await db.Set<TheoryDrawReceipt>().CountAsync(ct)).IsEqualTo(2);
            await Assert.That(await db.Set<TheoryIssueReceipt>().CountAsync(ct)).IsEqualTo(4);
            var store = new TheoryPaperStore(db);
            var paper = opened[0].View!.Paper!;
            var item = paper.Questions[0];
            var choice = item.Options.Single(option => option.Content == "right").Id;
            var answer = new TheoryAnswerCommand(actor.Id, activityId, paper.Id, item.Id, item.AnswerRevision,
                Guid.NewGuid(), [choice], now.AddSeconds(2));
            var answered = await store.AnswerAsync(answer, ct);
            await Assert.That(answered.Failure).IsNull();
            await Assert.That(answered.View!.Paper!.Score).IsNull();
            var replay = await store.AnswerAsync(answer, ct);
            await Assert.That(replay.Failure).IsNull();
            await Assert.That(await db.Set<TheoryAnswerReceipt>().CountAsync(ct)).IsEqualTo(1);
            await Assert.That((await store.AnswerAsync(answer with { RequestId = Guid.NewGuid() }, ct)).Failure).IsEqualTo(TheoryPaperFailure.AnswerConflict);
            await Assert.That((await store.SubmitAsync(new(actor.Id, activityId, paper.Id, paper.AnswerRevision, now.AddSeconds(3)), ct)).Failure)
                .IsEqualTo(TheoryPaperFailure.PaperConflict);
            var otherItem = answered.View.Paper.Questions[1];
            var competingAnswers = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var concurrentDb = new NoCtfDbContext(options);
                return await new TheoryPaperStore(concurrentDb).AnswerAsync(new(actor.Id, activityId, paper.Id,
                    otherItem.Id, otherItem.AnswerRevision, Guid.NewGuid(), [], now.AddSeconds(3)), ct);
            }));
            await Assert.That(competingAnswers.Count(result => result.Failure is null)).IsEqualTo(1);
            await Assert.That(competingAnswers.Count(result => result.Failure == TheoryPaperFailure.AnswerConflict)).IsEqualTo(1);
            answered = competingAnswers.Single(result => result.Failure is null);
            var submissionCommand = new TheoryPaperCommand(actor.Id, activityId, paper.Id, answered.View!.Paper!.AnswerRevision, now.AddSeconds(4));
            var submissions = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var concurrentDb = new NoCtfDbContext(options);
                return await new TheoryPaperStore(concurrentDb).SubmitAsync(submissionCommand, ct);
            }));
            await Assert.That(submissions.All(result => result.Failure is null)).IsTrue();
            var submitted = submissions[0];
            db.ChangeTracker.Clear(); // Resume in a fresh request view after the competing contexts committed.
            await Assert.That(submitted.Failure).IsNull();
            await Assert.That(submitted.View!.Paper!.Score).IsEqualTo(10L);
            await Assert.That(submitted.View.Paper.Questions).IsEmpty();
            await Assert.That(await db.Set<TheoryActivePaper>().CountAsync(ct)).IsEqualTo(0);
            foreach (var command in commands)
            {
                var repeated = await store.IssueAsync(command with { Now = now.AddSeconds(5) }, ct);
                await Assert.That(repeated.View!.Paper!.Id).IsEqualTo(paper.Id);
                await Assert.That(repeated.View.IssuedCount).IsEqualTo(1);
            }
            await Assert.That((await store.ReadAsync(new(other.Id, activityId, paper.Id, null, now), ct)).Failure)
                .IsEqualTo(TheoryPaperFailure.NotFoundOrForbidden);
            var delayed = await store.IssueAsync(new(actor.Id, activityId, null, null, now.AddSeconds(6), Guid.NewGuid()), ct);
            await Assert.That(delayed.Failure).IsEqualTo(TheoryPaperFailure.PaperConflict);
            var second = await store.IssueAsync(new(actor.Id, activityId, null, null, now.AddSeconds(6), Guid.NewGuid(), 1), ct);
            await Assert.That(second.Failure).IsNull();
            await Assert.That(second.View!.IssuedCount).IsEqualTo(2);
            await Assert.That(await db.Set<TheoryDrawReceipt>().CountAsync(ct)).IsEqualTo(4);
            // System timeout does not depend on the participant retaining account access.
            var blocked = await db.Users.SingleAsync(user => user.Id == actor.Id, ct); blocked.AccountStatus = UserAccountStatus.Banned;
            await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            await store.SweepAsync(activityId, now.AddHours(1), ct);
            var expired = await db.TheoryPapers.SingleAsync(row => row.Id == second.View.Paper!.Id, ct);
            await Assert.That(expired.State).IsEqualTo(TheoryPaperState.Submitted);
            await Assert.That(expired.SubmittedAt).IsEqualTo(now.AddSeconds(1806));
        });
    }

    private static User User(string name, UserRole role) => new() { Id = Guid.NewGuid(), UserName = name,
        NormalizedUserName = name.ToUpperInvariant(), Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@example.invalid".ToUpperInvariant(),
        Role = role, PasswordHash = "not-a-login" };
    private sealed class ContextFactory(DbContextOptions<NoCtfDbContext> options) : IDbContextFactory<NoCtfDbContext>
    {
        public NoCtfDbContext CreateDbContext() => new(options);
    }
}
