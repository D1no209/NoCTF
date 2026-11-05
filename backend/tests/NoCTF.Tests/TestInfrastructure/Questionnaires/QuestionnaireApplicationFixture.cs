using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Questionnaires.Activities;
using NoCTF.Infrastructure.Questionnaires.Sessions;

namespace NoCTF.Tests;

internal sealed class QuestionnaireApplicationFixture(ChallengeApplicationFixture application) : IAsyncDisposable
{
    public ChallengeApplicationFixture Application { get; } = application;
    public NoCtfDbContext Db => Application.Db;
    public Guid OwnerId => Application.OwnerId;
    public Guid FirstUserId { get; } = Guid.NewGuid();
    public Guid SecondUserId { get; } = Guid.NewGuid();
    public Guid ChallengeId { get; private set; }
    public DateTimeOffset Now => Application.Now;
    public QuestionnaireActivityStore Activities => new(Db);
    public QuestionnaireSessionStore Sessions => new(Db);
    public QuestionnaireReviewStore Review => new(Db, Application.Publisher);

    public static async Task<QuestionnaireApplicationFixture> CreateAsync(CancellationToken ct)
    {
        var application = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var fixture = new QuestionnaireApplicationFixture(application);
        try
        {
            foreach (var userId in new[] { fixture.FirstUserId, fixture.SecondUserId })
                fixture.Db.Users.Add(new() { Id = userId, UserName = userId.ToString("N"), NormalizedUserName = userId.ToString("N").ToUpperInvariant(),
                    Email = $"{userId:N}@example.test", NormalizedEmail = $"{userId:N}@EXAMPLE.TEST", Kind = UserKind.Human, AccountStatus = UserAccountStatus.Active });
            await fixture.Db.SaveChangesAsync(ct);
            var revision = new QuestionnaireRevision { Id = Guid.NewGuid(), Number = 1, CreatedAt = fixture.Now };
            for (var index = 0; index < 2; index++)
                revision.Questions.Add(new() { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Position = index, Title = $"Question {index}",
                    Body = "Choose the correct option.", Explanation = "Explanation for the reviewer.",
                    Options = [new() { Id = Guid.NewGuid(), Content = "correct", IsCorrect = true }, new() { Id = Guid.NewGuid(), Position = 1, Content = "incorrect" }] });
            var result = await new CreateChallengeTemplate(new ChallengeBankStore(fixture.Db, application.Publisher), application.Catalog)
                .ExecuteAsync(new(null, fixture.OwnerId, ChallengeVisibility.Private, "Questionnaire", null, "Knowledge",
                    new() { Capabilities = [new QuestionnaireCapabilityDefinition { CurrentRevisionId = revision.Id, CurrentRevision = revision }] }, fixture.Now), ct);
            if (!result.Succeeded) throw new InvalidOperationException($"Questionnaire creation failed: {result.State}.");
            fixture.ChallengeId = result.Template!.Id; return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task<Guid> PublishAsync(QuestionnaireActivityKind kind, int durationSeconds, int attempts, CancellationToken ct)
    {
        var saved = await Activities.SaveAsync(new(OwnerId, null,
            new("Independent activity", kind, Now, Now.AddHours(1), durationSeconds, attempts, QuestionnaireScoringMode.Highest, false, [])
            { SelectionKind = QuestionnaireSelectionKind.Fixed, FixedQuestionnaires = [new(ChallengeId, 10)] }, Now), ct);
        if (saved.Failure is not null) throw new InvalidOperationException($"Activity creation failed: {saved.Failure}.");
        var published = await Activities.TransitionAsync(new(OwnerId, saved.Activity!.Id, QuestionnaireActivityAction.Publish, Now), ct);
        if (published.Failure is not null) throw new InvalidOperationException($"Activity publication failed: {published.Failure}.");
        return saved.Activity.Id;
    }

    public ValueTask DisposeAsync() => Application.DisposeAsync();
}
