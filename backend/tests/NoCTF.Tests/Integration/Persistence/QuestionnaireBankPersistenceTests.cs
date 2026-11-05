using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class QuestionnaireBankPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Bank_permissions_revision_immutability_and_copy_provenance_apply_to_whole_questionnaires(CancellationToken ct)
    {
        await using var fixture = await QuestionnaireApplicationFixture.CreateAsync(ct);
        var bank = new ChallengeBankStore(fixture.Db, fixture.Application.Publisher);
        var original = (await bank.FindAsync(fixture.ChallengeId, fixture.OwnerId, false, false, ct))!;
        var originalRevision = original.Definition.Questionnaire!.CurrentRevisionId;
        var sources = original.Definition.Questionnaire.CurrentRevision.Questions.Select(question => question.SourceId).Order().ToArray();
        await Assert.That(await bank.FindAsync(fixture.ChallengeId, fixture.FirstUserId, false, false, ct)).IsNull();
        var definition = ChallengeDefinitionContractMapper.ToDomain(fixture.ChallengeId, ChallengeDefinitionContractMapper.FromDomain(original.Definition));
        await Assert.That((await bank.UpdateAsync(new(fixture.ChallengeId, fixture.FirstUserId, false, original.Visibility, original.Title,
            "Unauthorized edit", original.Direction, definition, fixture.Now.AddSeconds(1)), ct)).State).IsEqualTo(ChallengeTemplateWriteState.NotFoundOrForbidden);
        await Assert.That((await bank.UpdatePermissionsAsync(fixture.ChallengeId, fixture.OwnerId, false, [fixture.FirstUserId], fixture.Now.AddSeconds(2), ct)).State)
            .IsEqualTo(ChallengeTemplateWriteState.RoleNotEligible);
        (await fixture.Db.Users.SingleAsync(user => user.Id == fixture.FirstUserId, ct)).Role = UserRole.Organizer;
        await fixture.Db.SaveChangesAsync(ct);
        await Assert.That((await bank.UpdatePermissionsAsync(fixture.ChallengeId, fixture.OwnerId, false, [fixture.FirstUserId], fixture.Now.AddSeconds(3), ct)).State)
            .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
        definition.Questionnaire!.CurrentRevision.Questions[0].Body = "Edited questionnaire content.";
        var updated = await bank.UpdateAsync(new(fixture.ChallengeId, fixture.FirstUserId, false, original.Visibility, original.Title,
            original.Description, original.Direction, definition, fixture.Now.AddSeconds(4)), ct);
        await Assert.That(updated.State).IsEqualTo(ChallengeTemplateWriteState.Succeeded);
        await Assert.That(updated.Template!.Definition.Questionnaire!.CurrentRevisionId).IsNotEqualTo(originalRevision);
        fixture.Db.ChangeTracker.Clear();
        var oldRevision = await fixture.Db.Set<QuestionnaireRevision>().AsSplitQuery().SingleAsync(value => value.Id == originalRevision, ct);
        await Assert.That(oldRevision.Questions.All(question => question.Body == "Choose the correct option.")).IsTrue();
        var source = (await bank.FindAsync(fixture.ChallengeId, fixture.FirstUserId, false, false, ct))!;
        var copied = await new ChallengeTemplateCopyStore(fixture.Db, new ChallengeMaterialCloner(fixture.Db, new FileReferenceLock()), fixture.Application.Publisher)
            .CopyAsync(new(fixture.ChallengeId, fixture.FirstUserId, false, false, fixture.Now.AddSeconds(5)), ct);
        await Assert.That(copied.Failure).IsNull();
        var copy = (await bank.FindAsync(copied.Copy!.ChallengeId, fixture.FirstUserId, false, false, ct))!;
        await Assert.That(copy.OwnerId).IsEqualTo(fixture.FirstUserId);
        await Assert.That(copy.Definition.Questionnaire!.CurrentRevisionId).IsNotEqualTo(source.Definition.Questionnaire!.CurrentRevisionId);
        await Assert.That(copy.Definition.Questionnaire.CurrentRevision.Questions.Select(question => question.SourceId).Order()).IsEquivalentTo(sources);
        await Assert.That(await bank.FindAsync(copy.Id, fixture.SecondUserId, false, false, ct)).IsNull();
        oldRevision.Questions[0].Body = "Attempt to overwrite an old revision.";
        await Assert.That(async () => await fixture.Db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
    }
}
