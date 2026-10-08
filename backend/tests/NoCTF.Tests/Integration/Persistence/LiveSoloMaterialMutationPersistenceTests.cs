using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Attachments;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.LiveSolo.Templates;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloMaterialMutationPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Preparing_and_active_rounds_freeze_text_flags_attachments_and_entries_until_the_scope_ends(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct);
            var gate = new LiveSoloMaterialMutationGate(fixture.Db);
            var bank = new ChallengeBankStore(fixture.Db, material: gate);
            var template = fixture.Templates[0];
            var command = new UpdateChallengeTemplateCommand(template.Id, fixture.Owner.Id, true, GameMode.LiveSolo, ChallengeVisibility.Private,
                template.Title, "changed active text", template.Direction, template.Definition!, fixture.Now);
            await Assert.That(async () => await bank.UpdateAsync(command, ct)).Throws<ChallengeMaterialMutationException>();
            var flags = new ChallengeFlagManagementStore(fixture.Db, new ChallengeRuntimeTemplateCatalog(), gate);
            var flag = await fixture.Db.ChallengeFlags.SingleAsync(x => x.ChallengeId == template.Id, ct);
            await Assert.That(async () => await flags.DeleteAsync(ChallengeFlagScope.Template(template.Id), flag.Id, fixture.Owner.Id, true, fixture.Now, ct))
                .Throws<ChallengeMaterialMutationException>();
            var attachments = new ChallengeAttachmentStore(fixture.Db, new FileReferenceLock(), material: gate);
            await Assert.That(async () => await attachments.CanWriteAsync(template.Id, fixture.Owner.Id, true, ct)).Throws<ChallengeMaterialMutationException>();
            var entries = new ChallengeManagementStore(fixture.Db, Substitute.For<IPostCommitMessagePublisher>(), new ChallengeRuntimeTemplateCatalog(), material: gate);
            await Assert.That(async () => await entries.SoftDeleteAsync(fixture.Competition.Id, fixture.Entries[0].Id, fixture.Now, ct))
                .Throws<ChallengeMaterialMutationException>();
            await Assert.That(template.Description).IsEqualTo("secret body 0");
            await Assert.That(flag.DeletedAt).IsNull();
            fixture.Round.State = LiveSoloRoundState.Canceled; await fixture.Db.SaveChangesAsync(ct);
            var changed = await bank.UpdateAsync(command, ct);
            await Assert.That(changed.State).IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That((await flags.DeleteAsync(ChallengeFlagScope.Template(template.Id), flag.Id, fixture.Owner.Id, true, fixture.Now, ct)))
                .IsEqualTo(ChallengeFlagMutationState.Updated);
            await Assert.That(await attachments.CanWriteAsync(template.Id, fixture.Owner.Id, true, ct)).IsTrue();
        });
    }
}
