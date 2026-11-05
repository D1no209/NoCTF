using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTemplateCopyPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Bank_copies_preserve_permissions_file_identity_flags_and_canonical_source_across_independent_copies(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var db = fixture.Db; var actor = fixture.OwnerId; var sourceOwner = Guid.NewGuid();
        db.Users.Add(new User { Id = sourceOwner, UserName = "source-owner", NormalizedUserName = "SOURCE-OWNER", Email = "source@example.test",
            Role = UserRole.Organizer, AccountStatus = UserAccountStatus.Active });
        var source = new Challenge { Id = Guid.NewGuid(), OwnerId = sourceOwner, Title = "Source", Description = "Original", Direction = "Web",
            Visibility = ChallengeVisibility.Shared, Definition = new() { Capabilities = [new BreakCapabilityDefinition()] } };
        var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = "copy/source.pdf", FileName = "source.pdf", ContentType = "application/pdf",
            ByteLength = 4, Sha256 = System.Security.Cryptography.SHA256.HashData("file"u8), CreatedAt = fixture.Now };
        var attachment = new ChallengeAttachment { Id = Guid.NewGuid(), ChallengeId = source.Id, File = file, CreatedAt = fixture.Now };
        source.Attachments.Add(attachment);
        var flag = new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = source.Id, Flag = "flag{source}",
            FlagSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash("flag{source}"), CreatedAt = fixture.Now };
        db.Challenges.Add(source); db.ChallengeFlags.Add(flag); await db.SaveChangesAsync(ct);
        var manager = new CopyChallengeTemplate(new ChallengeTemplateCopyStore(db,
            new ChallengeMaterialCloner(db, new FileReferenceLock()), fixture.Publisher));
        var command = new CopyChallengeTemplateCommand(source.Id, actor, true, true, fixture.Now);
        await Assert.That((await manager.ExecuteAsync(command, ct)).Failure).IsEqualTo(ChallengeTemplateCopyFailure.AttachmentsForbidden);
        await Assert.That((await manager.ExecuteAsync(command with { CopyAttachments = false }, ct)).Failure).IsEqualTo(ChallengeTemplateCopyFailure.FlagsForbidden);
        source.Managers.Add(new() { ChallengeId = source.Id, UserId = actor }); await db.SaveChangesAsync(ct);
        var first = await manager.ExecuteAsync(command, ct); await Assert.That(first.Failure).IsNull();
        var copy = await db.Challenges.Include(value => value.Attachments).AsSplitQuery().SingleAsync(value => value.Id == first.Copy!.ChallengeId, ct);
        await Assert.That(copy.CatalogScope).IsEqualTo(ChallengeCatalogScope.QuestionBank);
        await Assert.That(copy.Definition!.Capabilities.Single().Kind).IsEqualTo(ChallengeCapabilityKind.Break);
        await Assert.That(copy.Attachments.Single().FileId).IsEqualTo(file.Id);
        await Assert.That(copy.Attachments.Single().Id == attachment.Id).IsFalse();
        await Assert.That((await db.ChallengeFlags.SingleAsync(value => value.ChallengeId == copy.Id, ct)).Flag).IsEqualTo(flag.Flag);
        source.Description = "Changed"; await db.SaveChangesAsync(ct);
        await Assert.That(copy.Description).IsEqualTo("Original");
        var second = await manager.ExecuteAsync(command, ct);
        var repeatedCopy = await manager.ExecuteAsync(command with { SourceChallengeId = copy.Id }, ct);
        await Assert.That(second.Failure).IsNull(); await Assert.That(repeatedCopy.Failure).IsNull();
        await Assert.That((await db.Challenges.SingleAsync(value => value.Id == second.Copy!.ChallengeId, ct)).Description).IsEqualTo("Changed");
        await Assert.That(first.Copy!.CanonicalChallengeId).IsEqualTo(source.Id);
        await Assert.That(second.Copy!.CanonicalChallengeId).IsEqualTo(source.Id);
        await Assert.That(repeatedCopy.Copy!.CanonicalChallengeId).IsEqualTo(source.Id);
        flag.SpecificationKind = SpecificationKind.Attachment; flag.SpecificationId = attachment.Id; await db.SaveChangesAsync(ct);
        await Assert.That((await manager.ExecuteAsync(command with { CopyAttachments = false }, ct)).Failure).IsEqualTo(ChallengeTemplateCopyFailure.AttachmentsRequired);
        var linkedCopy = await manager.ExecuteAsync(command, ct); await Assert.That(linkedCopy.Failure).IsNull();
        var copiedAttachment = await db.Set<ChallengeAttachment>().SingleAsync(value => value.ChallengeId == linkedCopy.Copy!.ChallengeId, ct);
        var copiedFlag = await db.ChallengeFlags.SingleAsync(value => value.ChallengeId == linkedCopy.Copy!.ChallengeId, ct);
        await Assert.That(copiedFlag.SpecificationId).IsEqualTo(copiedAttachment.Id);
        var before = await db.ChallengeSources.CountAsync(ct);
        db.Set<ChallengeManager>().Add(new() { ChallengeId = Guid.NewGuid(), UserId = actor });
        await Assert.That(async () => { await manager.ExecuteAsync(command, ct); }).Throws<DbUpdateException>(); db.ChangeTracker.Clear();
        await Assert.That(await db.ChallengeSources.CountAsync(ct)).IsEqualTo(before);
        await Assert.That(await db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await db.Files.CountAsync(ct)).IsEqualTo(1);
    }
}
