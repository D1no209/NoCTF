using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using NSubstitute;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Storage;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.LiveSolo.Templates;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloTemplateCopyPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Copies_preserve_source_permissions_immutable_file_references_flags_and_canonical_scheduling_identity(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options); await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow;
            User User(string name) => new() { Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.test",
                PasswordHash = "unused", Role = UserRole.Organizer, AccountStatus = UserAccountStatus.Active, CreatedAt = now };
            var actor = User("copy-manager"); var sourceOwner = User("source-owner");
            var competition = new LiveSoloCompetition { Id = Guid.NewGuid(), OwnerId = actor.Id, Title = "Copy test", StartAt = now, EndAt = now.AddHours(1),
                CreatedAt = now, UpdatedAt = now, FlagDerivationSecret = new byte[32], ModeConfiguration = new LiveSoloCompetitionModeConfiguration() };
            var source = new CtfChallenge { Id = Guid.NewGuid(), OwnerId = sourceOwner.Id, Title = "Source", Description = "# original", Direction = "Web",
                Visibility = ChallengeVisibility.Shared, Definition = new CtfChallengeDefinition(), CreatedAt = now, UpdatedAt = now };
            var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = "copy/source.pdf", FileName = "source.pdf", ContentType = "application/pdf", ByteLength = 4,
                Sha256 = new byte[32], CreatedAt = now };
            var attachment = new ChallengeAttachment { Id = Guid.NewGuid(), ChallengeId = source.Id, File = file, CreatedAt = now };
            source.Attachments.Add(attachment);
            var flag = new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = source.Id, Flag = "flag{source}",
                FlagSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash("flag{source}"), CreatedAt = now };
            db.Users.AddRange(actor, sourceOwner); db.Competitions.Add(competition); db.Challenges.Add(source); db.ChallengeFlags.Add(flag);
            await db.SaveChangesAsync(ct);
            CopyLiveSoloTemplate Manager(NoCtfDbContext context)
            {
                var messages = Substitute.For<IPostCommitMessagePublisher>();
                return new(new LiveSoloTemplateCopyStore(context, new CompetitionModerationAuthorizer(context),
                    new ChallengeManagementStore(context, messages, new ChallengeRuntimeTemplateCatalog()), new GameModeChallengeConfigurationCatalog(),
                    new FileReferenceLock(), messages));
            }
            var manager = Manager(db);
            var command = new CopyLiveSoloTemplateCommand(competition.Id, source.Id, actor.Id, true, true, [" Web ", "web"], now);
            await Assert.That((await manager.ExecuteAsync(command, ct)).Failure).IsEqualTo(LiveSoloTemplateCopyFailure.AttachmentsForbidden);
            await Assert.That((await manager.ExecuteAsync(command with { CopyAttachments = false }, ct)).Failure).IsEqualTo(LiveSoloTemplateCopyFailure.FlagsForbidden);
            await Assert.That(await db.Set<LiveSoloChallenge>().CountAsync(ct)).IsEqualTo(0);
            source.Managers.Add(new() { ChallengeId = source.Id, UserId = actor.Id }); db.Set<ChallengeManager>().Add(source.Managers.Single());
            await db.SaveChangesAsync(ct);
            var first = await manager.ExecuteAsync(command, ct);
            await Assert.That(first.Failure).IsNull();
            var copy = await db.Challenges.Include(x => x.Attachments).SingleAsync(x => x.Id == first.Copy!.ChallengeId, ct);
            await Assert.That(copy.Mode).IsEqualTo(GameMode.LiveSolo); await Assert.That(source.Mode).IsEqualTo(GameMode.Ctf);
            await Assert.That(copy.Attachments.Single().FileId).IsEqualTo(file.Id);
            await Assert.That(copy.Attachments.Single().Id).IsNotEqualTo(attachment.Id);
            await Assert.That((await db.CompetitionChallenges.SingleAsync(x => x.Id == first.Copy!.CompetitionChallengeId, ct)).Tags.Select(x => x.Name)).IsEquivalentTo(["Web"]);
            await Assert.That((await db.ChallengeFlags.SingleAsync(x => x.ChallengeId == copy.Id, ct)).Flag).IsEqualTo(flag.Flag);
            source.Description = "# changed"; await db.SaveChangesAsync(ct);
            await Assert.That(copy.Description).IsEqualTo("# original");
            var second = await manager.ExecuteAsync(command, ct);
            var reCopy = await manager.ExecuteAsync(command with { SourceChallengeId = first.Copy!.ChallengeId }, ct);
            await Assert.That(second.Failure).IsNull(); await Assert.That(reCopy.Failure).IsNull();
            await Assert.That(first.Copy.CanonicalChallengeId).IsEqualTo(source.Id);
            await Assert.That(reCopy.Copy!.CanonicalChallengeId).IsEqualTo(source.Id);
            var matchStore = new LiveSoloMatchStore(db, new CompetitionModerationAuthorizer(db), Substitute.For<ILiveSoloMediaGateway>(), Substitute.For<ILiveSoloEgressGateway>(),
                Substitute.For<ILiveSoloRuntimePreparation>(), Substitute.For<IPostCommitMessagePublisher>());
            var duplicateSource = await matchStore.SaveGroupAsync(competition.Id, actor.Id, new(null, "Duplicate source", false, null, [
                new(first.Copy.CompetitionChallengeId, null), new(second.Copy!.CompetitionChallengeId, null)], null), now, ct);
            await Assert.That(duplicateSource.Failure).IsEqualTo(LiveSoloFailure.InvalidConfiguration);
            await Assert.That(await db.LiveSoloQuestionGroups.CountAsync(ct)).IsEqualTo(0);
            flag.SpecificationKind = SpecificationKind.Attachment; flag.SpecificationId = attachment.Id; await db.SaveChangesAsync(ct);
            var withoutMatchingFile = await manager.ExecuteAsync(command with { CopyAttachments = false }, ct);
            await Assert.That(withoutMatchingFile.Failure).IsEqualTo(LiveSoloTemplateCopyFailure.AttachmentsRequired);
            await Assert.That(await db.Set<LiveSoloChallenge>().CountAsync(ct)).IsEqualTo(3);
            var random = await manager.ExecuteAsync(command, ct);
            await Assert.That(random.Failure).IsNull();
            var randomAttachment = await db.Set<ChallengeAttachment>().SingleAsync(x => x.ChallengeId == random.Copy!.ChallengeId, ct);
            var randomFlag = await db.ChallengeFlags.SingleAsync(x => x.ChallengeId == random.Copy!.ChallengeId, ct);
            await Assert.That(randomFlag.SpecificationId).IsEqualTo(randomAttachment.Id);
            await Assert.That(randomAttachment.Id).IsNotEqualTo(attachment.Id);
            var failure = new FailCommit();
            var failureOptions = new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(failure).Options;
            await using (var failing = new NoCtfDbContext(failureOptions))
            {
                failure.Armed = true;
                await Assert.That(async () => await Manager(failing).ExecuteAsync(command, ct)).Throws<InvalidOperationException>();
            }
            await Assert.That(await db.Set<LiveSoloChallenge>().CountAsync(ct)).IsEqualTo(4);
            await Assert.That(await db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(4);
            await Assert.That(await db.LiveSoloChallengeSources.CountAsync(ct)).IsEqualTo(4);
            db.ChallengeFlags.Remove(flag); db.Set<ChallengeAttachment>().Remove(attachment);
            db.Set<ChallengeManager>().RemoveRange(source.Managers); db.Remove(source.Definition!);
            db.Challenges.Remove(source); await db.SaveChangesAsync(ct);
            await Assert.That((await db.LiveSoloChallengeSources.SingleAsync(x => x.ChallengeId == first.Copy.ChallengeId, ct)).CanonicalChallengeId).IsEqualTo(source.Id);
            await Assert.That(await db.Files.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await db.Set<LiveSoloChallenge>().CountAsync(ct)).IsEqualTo(4);
        });
    }
    private sealed class FailCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; throw new InvalidOperationException("Injected template copy commit failure"); }
            return ValueTask.FromResult(result);
        }
    }
}
