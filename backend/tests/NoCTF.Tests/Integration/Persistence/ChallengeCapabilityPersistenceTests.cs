using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Unit.Gameplay;
using Testcontainers.PostgreSql;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Infrastructure.Questionnaires.Activities;
using NoCTF.Infrastructure.Questionnaires.Sessions;
using NoCTF.Domain.Questionnaires.Sessions;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeCapabilityPersistenceTests
{
    [Test, Arguments(QuestionnaireSelectionKind.Draw), Arguments(QuestionnaireSelectionKind.Fixed)]
    public async Task Independent_questionnaire_replay_and_activity_void_preserve_consistent_facts(QuestionnaireSelectionKind selection, CancellationToken ct)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("questionnaire_test").Build();
        await postgres.StartAsync(ct);
        await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var owner = new User { Id = Guid.NewGuid(), UserName = "owner", NormalizedUserName = "OWNER", Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", Role = UserRole.Organizer };
        var student = new User { Id = Guid.NewGuid(), UserName = "student", NormalizedUserName = "STUDENT", Email = "student@example.test", NormalizedEmail = "STUDENT@EXAMPLE.TEST", Kind = UserKind.Human };
        var revision = new QuestionnaireRevision { Id = Guid.NewGuid(), Number = 1, CreatedAt = now };
        for (var position = 0; position < 2; position++)
        {
            var question = new QuestionnaireQuestion
            {
                Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Position = position, Title = $"Question {position}", Body = "Choose one option.",
                Options = [new() { Id = Guid.NewGuid(), Content = "Correct", IsCorrect = true }, new() { Id = Guid.NewGuid(), Content = "Wrong", Position = 1 }]
            };
            revision.Questions.Add(question);
            db.Add(new QuestionnaireQuestionSource { Id = question.SourceId });
        }
        var template = new Challenge { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "Two questions", Definition = new()
            { Capabilities = [new QuestionnaireCapabilityDefinition { CurrentRevisionId = revision.Id, CurrentRevision = revision }] } };
        ChallengeDefinitionGraph.AssignChallengeId(template.Definition, template.Id);
        db.Users.AddRange(owner, student); db.Challenges.Add(template);
        await db.SaveChangesAsync(ct);
        var activities = new QuestionnaireActivityStore(db);
        var saved = await activities.SaveAsync(new(owner.Id, null,
            new("Independent examination", selection == QuestionnaireSelectionKind.Fixed ? QuestionnaireActivityKind.Practice : QuestionnaireActivityKind.Examination,
                now.AddMinutes(-1), now.AddHours(1), 600, selection == QuestionnaireSelectionKind.Fixed ? 2 : 1,
                QuestionnaireScoringMode.Highest, false, selection == QuestionnaireSelectionKind.Fixed ? [] : [new(QuestionnaireQuestionKind.SingleChoice, null, 2, 10, [])])
                { SelectionKind = selection, FixedQuestionnaires = selection == QuestionnaireSelectionKind.Fixed ? [new(template.Id, 10)] : [] }, now), ct);
        await Assert.That(saved.Failure).IsNull();
        var activityId = saved.Activity!.Id;
        var published = await activities.TransitionAsync(new(owner.Id, activityId, QuestionnaireActivityAction.Publish, now), ct);
        await Assert.That(published.Failure).IsNull();
        var changedContent = (QuestionnaireCapabilityDefinition)ChallengeCapabilityCopy.Copy(template.Definition.Questionnaire!);
        changedContent.CurrentRevision.Number = 2; changedContent.CurrentRevision.CreatedAt = now.AddSeconds(1);
        foreach (var question in changedContent.CurrentRevision.Questions) question.Body = "Edited after publication.";
        var capability = template.Definition.Questionnaire!;
        capability.CurrentRevision = changedContent.CurrentRevision; capability.CurrentRevisionId = changedContent.CurrentRevisionId;
        ChallengeDefinitionGraph.AssignChallengeId(template.Definition, template.Id); db.Add(changedContent.CurrentRevision);
        await db.SaveChangesAsync(ct);
        var papers = new QuestionnaireSessionStore(db);
        var issued = await papers.IssueAsync(new(student.Id, activityId, null, null, now.AddSeconds(1), Guid.NewGuid()), ct);
        await Assert.That(issued.Failure).IsNull();
        var paper = issued.View!.Session!;
        await Assert.That(paper.Questions.All(question => question.Body == "Choose one option.")).IsTrue();
        var first = paper.Questions[0];
        var answered = await papers.AnswerAsync(new(student.Id, activityId, paper.Id, first.Id, first.AnswerRevision, Guid.NewGuid(),
            [first.Options.Single(option => option.Content == "Correct").Id], now.AddSeconds(2)), ct);
        await Assert.That(answered.Failure).IsNull();
        var submitted = await papers.SubmitAsync(new(student.Id, activityId, paper.Id, answered.View!.Session!.AnswerRevision, now.AddSeconds(3)), ct);
        await Assert.That(submitted.Failure).IsNull();
        await Assert.That(submitted.View!.EffectiveScore.Points).IsEqualTo(10L);
        db.ChangeTracker.Clear();
        var fact = await db.GameplayFacts.OfType<QuestionnaireAttemptGameplayFact>().AsSplitQuery().SingleAsync(ct);
        await Assert.That(fact.CompetitionId).IsNull();
        await Assert.That(fact.CompetitionChallengeId).IsNull();
        await Assert.That(fact.TeamId).IsNull();
        await Assert.That(fact.QuestionnaireAssignmentId).IsNotNull();
        await Assert.That(fact.CapabilityOutcomes.OfType<QuestionnaireOutcome>().Single().Questions.Count).IsEqualTo(2);
        await Assert.That(await db.Competitions.CountAsync(ct)).IsEqualTo(0);
        var repeated = await papers.SubmitAsync(new(student.Id, activityId, paper.Id, submitted.View.Session!.AnswerRevision, now.AddSeconds(4)), ct);
        await Assert.That(repeated.Failure).IsNull();
        await Assert.That(repeated.View!.EffectiveScore.Points).IsEqualTo(10L);
        await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(1);
        var active = await papers.IssueAsync(new(selection == QuestionnaireSelectionKind.Fixed ? student.Id : owner.Id,
            activityId, null, null, now.AddSeconds(5), Guid.NewGuid(), ExpectedIssuedCount: selection == QuestionnaireSelectionKind.Fixed ? 1 : 0), ct);
        await Assert.That(active.Failure).IsNull();
        await Assert.That(active.View!.Session!.Questions.All(question => question.Body == "Choose one option.")).IsTrue();
        await Assert.That(await db.Set<QuestionnaireActiveSession>().CountAsync(ct)).IsEqualTo(1);
        var voided = await activities.TransitionAsync(new(owner.Id, activityId, QuestionnaireActivityAction.Void, now.AddSeconds(6), Reason: "Invalid examination"), ct);
        await Assert.That(voided.Failure).IsNull();
        db.ChangeTracker.Clear();
        await Assert.That(await db.QuestionnaireSessions.CountAsync(paper => paper.State == QuestionnaireSessionState.Voided, ct)).IsEqualTo(2);
        await Assert.That(await db.Set<QuestionnaireActiveSession>().CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(1);
        var rejected = await db.GameplayFacts.AsSplitQuery().SingleAsync(ct);
        await Assert.That(rejected.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(rejected.CapabilityOutcomes.OfType<QuestionnaireOutcome>().Single().Questions.Count).IsEqualTo(2);
    }

    [Test]
    public async Task One_template_can_be_referenced_by_ctf_and_awdp_with_relational_capabilities(CancellationToken ct)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("capability_test").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var owner = new User { Id = Guid.NewGuid(), UserName = "author", NormalizedUserName = "AUTHOR", Email = "author@example.test", NormalizedEmail = "AUTHOR@EXAMPLE.TEST" };
        var template = new Challenge { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "Reusable", Definition = ChallengeCapabilityTests.Repairable() };
        template.Definition.Break!.EnvironmentTargets.Add(new() { ServiceName = "web", VariableName = "FLAG" });
        var ctf = new CtfCompetition { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "CTF", ModeConfiguration = new CtfCompetitionModeConfiguration() };
        var awdp = new AwdpCompetition { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "AWDP", ModeConfiguration = new AwdpCompetitionModeConfiguration() };
        db.Users.Add(owner); db.Challenges.Add(template); db.Competitions.AddRange(ctf, awdp);
        db.CompetitionChallenges.AddRange(
            new CtfCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = ctf.Id, ChallengeId = template.Id, Rules = new CtfCompetitionChallengeRules() },
            new AwdpCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = awdp.Id, ChallengeId = template.Id, Rules = new AwdpCompetitionChallengeRules() });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var loaded = await db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == template.Id, ct);
        await Assert.That(loaded.Definition!.Capabilities.Count).IsEqualTo(3);
        await Assert.That(loaded.Definition.Fix!.TargetServiceName).IsEqualTo("web");
        await Assert.That(loaded.Definition.Break!.EnvironmentTargets.Single().VariableName).IsEqualTo("FLAG");
        await Assert.That(loaded.Definition.Check!.Requirements.Count).IsEqualTo(2);
        await Assert.That(await db.CompetitionChallenges.CountAsync(value => value.ChallengeId == template.Id, ct)).IsEqualTo(2);
        var revisionId = Guid.NewGuid();
        var quiz = new Challenge { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "Questionnaire", Definition = new()
        {
            Capabilities = [new QuestionnaireCapabilityDefinition { CurrentRevisionId = revisionId, CurrentRevision = new()
            {
                Id = revisionId, Number = 1, Questions = [new()
                {
                    Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Title = "Choice", Body = "Choose the correct option.",
                    Options = [new() { Id = Guid.NewGuid(), Content = "First", IsCorrect = true }, new() { Id = Guid.NewGuid(), Content = "Second", Position = 1 }]
                }]
            } }]
        } };
        ChallengeDefinitionGraph.AssignChallengeId(quiz.Definition, quiz.Id);
        db.Add(new QuestionnaireQuestionSource { Id = quiz.Definition.Questionnaire!.CurrentRevision.Questions[0].SourceId });
        db.Challenges.Add(quiz);
        await db.SaveChangesAsync(ct);
        quiz.Definition.Questionnaire!.CurrentRevision.Questions[0].Body = "Changed after publication";
        await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        db.ChangeTracker.Clear();
        db.Set<BreakCapabilityDefinition>().Add(new() { ChallengeId = template.Id });
        await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<DbUpdateException>();
    }
}
