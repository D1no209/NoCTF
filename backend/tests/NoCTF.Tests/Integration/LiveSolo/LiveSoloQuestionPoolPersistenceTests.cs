using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.LiveSolo.Templates;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloQuestionPoolPersistenceTests
{
    private static LiveSoloQuestionPoolStore Pool(NoCtfDbContext db)
    {
        var messages = Substitute.For<IPostCommitMessagePublisher>();
        return new(db, new CompetitionModerationAuthorizer(db), new ChallengeManagementStore(db, messages, new ChallengeRuntimeTemplateCatalog()),
            new GameModeChallengeConfigurationCatalog(), new LiveSoloMaterialCloner(db, new FileReferenceLock()), messages);
    }

    [Test, Timeout(300_000)]
    public async Task Ordinary_competition_owner_can_create_local_material_outside_the_bank_and_export_needs_bank_role(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var result = await Pool(f.Db).CreateAsync(new(f.Competition.Id, f.Owner.Id, "Local question", "local body", "Web", new(), f.Now), ct);
            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.Question!.Ownership).IsEqualTo(LiveSoloMaterialOwnership.CompetitionOwned);
            var bank = new ChallengeBankStore(f.Db);
            await Assert.That(await bank.FindAsync(result.Question.ChallengeId, f.Owner.Id, true, false, ct)).IsNull();
            var direct = await new ChallengeManagementStore(f.Db, Substitute.For<IPostCommitMessagePublisher>(), new ChallengeRuntimeTemplateCatalog())
                .CreateAsync(new(null, f.Competition.Id, result.Question.ChallengeId, 99, f.Now), new LiveSoloCompetitionChallengeRules(), ct);
            await Assert.That(direct.Challenge).IsNull();
            var listed = await bank.ListPageAsync(new(f.Owner.Id, true, false, null, null, 0, 100, false), ct);
            await Assert.That(listed.Items.Any(x => x.Id == result.Question.ChallengeId)).IsFalse();
            var command = new ExportLiveSoloPoolQuestion(f.Competition.Id, result.Question.Id, f.Owner.Id, result.Question.ConcurrencyStamp, result.Question.MaterialStamp, f.Now);
            await Assert.That((await Pool(f.Db).ExportAsync(command, ct)).Failure).IsEqualTo(LiveSoloPoolFailure.Forbidden);
            var owner = await f.Db.Users.SingleAsync(x => x.Id == f.Owner.Id, ct);
            owner.Role = UserRole.Organizer; await f.Db.SaveChangesAsync(ct);
            var exported = await Pool(f.Db).ExportAsync(command, ct);
            await Assert.That(exported.Failure).IsNull();
            var saved = await bank.FindAsync(exported.ChallengeId!.Value, f.Owner.Id, false, false, ct);
            await Assert.That(saved!.Title).IsEqualTo("Local question");
            await Assert.That((await f.Db.CompetitionChallenges.SingleAsync(x => x.Id == result.Question.Id, ct)).ChallengeId).IsEqualTo(result.Question.ChallengeId);
            await Assert.That(saved.Id).IsNotEqualTo(result.Question.ChallengeId);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Deploy_follows_bank_and_first_real_edit_detaches_without_changing_other_competitions_or_question_identity(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var other = new LiveSoloCompetition { Id = Guid.NewGuid(), OwnerId = f.Owner.Id, Title = "Other competition", StartAt = f.Now,
                EndAt = f.Now.AddHours(1), CreatedAt = f.Now, UpdatedAt = f.Now, FlagDerivationSecret = new byte[32], ModeConfiguration = new LiveSoloCompetitionModeConfiguration() };
            f.Db.Add(other); await f.Db.SaveChangesAsync(ct);
            var pool = Pool(f.Db); var source = f.Templates[0];
            var original = await pool.DeployAsync(new(f.Competition.Id, f.Owner.Id, source.Id, source.ConcurrencyStamp, f.Now), ct);
            var second = await pool.DeployAsync(new(other.Id, f.Owner.Id, source.Id, source.ConcurrencyStamp, f.Now), ct);
            await Assert.That(second.Failure).IsNull();
            await Assert.That(original.Question!.Id).IsEqualTo(f.Entries[0].Id);
            source.Description = "updated bank body"; await f.Db.SaveChangesAsync(ct);
            var current = (await pool.ListAsync(f.Competition.Id, f.Owner.Id, ct))!.Single(x => x.Id == original.Question.Id);
            var unchanged = await pool.EditAsync(new(f.Competition.Id, current.Id, f.Owner.Id, current.ConcurrencyStamp, current.MaterialStamp,
                current.Title, current.Description, current.Direction, new(), f.Now), ct);
            await Assert.That(unchanged.Question!.Ownership).IsEqualTo(LiveSoloMaterialOwnership.FollowingQuestionBank);
            var edited = await pool.EditAsync(new(f.Competition.Id, current.Id, f.Owner.Id, current.ConcurrencyStamp, current.MaterialStamp,
                current.Title, "edited locally", current.Direction, new(), f.Now), ct);
            await Assert.That(edited.Failure).IsNull();
            await Assert.That(edited.Question!.Id).IsEqualTo(current.Id);
            await Assert.That(edited.Question.Ownership).IsEqualTo(LiveSoloMaterialOwnership.CompetitionOwned);
            await Assert.That(edited.Question.ChallengeId).IsNotEqualTo(source.Id);
            await Assert.That(edited.Question.SourceTemplateId).IsEqualTo(source.Id);
            var copiedFlag = await f.Db.ChallengeFlags.SingleAsync(x => x.ChallengeId == edited.Question.ChallengeId, ct);
            await Assert.That(copiedFlag.Flag).IsEqualTo("flag{first}");
            source.Description = "later bank body"; await f.Db.SaveChangesAsync(ct);
            var firstPool = (await pool.ListAsync(f.Competition.Id, f.Owner.Id, ct))!;
            var otherPool = (await pool.ListAsync(other.Id, f.Owner.Id, ct))!;
            await Assert.That(firstPool.Single(x => x.Id == current.Id).Description).IsEqualTo("edited locally");
            await Assert.That(otherPool.Single().Description).IsEqualTo("later bank body");
            var repeat = await pool.DeployAsync(new(f.Competition.Id, f.Owner.Id, source.Id, source.ConcurrencyStamp, f.Now), ct);
            await Assert.That(repeat.Question!.Id).IsEqualTo(current.Id);
            await Assert.That(await f.Db.Set<LiveSoloQuestionMaterial>().CountAsync(ct)).IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Current_active_round_blocks_content_edits_and_shared_source_in_another_active_competition_does_not_block_detachment(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var other = new LiveSoloCompetition { Id = Guid.NewGuid(), OwnerId = f.Owner.Id, Title = "Second", StartAt = f.Now,
                EndAt = f.Now.AddHours(1), CreatedAt = f.Now, UpdatedAt = f.Now, FlagDerivationSecret = new byte[32], ModeConfiguration = new LiveSoloCompetitionModeConfiguration() };
            f.Db.Add(other); await f.Db.SaveChangesAsync(ct);
            var pool = Pool(f.Db); var source = f.Templates[0];
            var deployed = await pool.DeployAsync(new(other.Id, f.Owner.Id, source.Id, source.ConcurrencyStamp, f.Now), ct);
            await f.PrepareAsync(ct);
            var current = (await pool.ListAsync(f.Competition.Id, f.Owner.Id, ct))!.Single(x => x.Id == f.Entries[0].Id);
            await Assert.That((await pool.EditAsync(new(f.Competition.Id, current.Id, f.Owner.Id, current.ConcurrencyStamp, current.MaterialStamp,
                current.Title, "not allowed", current.Direction, new(), f.Now), ct)).Failure).IsEqualTo(LiveSoloPoolFailure.ActiveRound);
            var target = deployed.Question!;
            var edited = await pool.EditAsync(new(other.Id, target.Id, f.Owner.Id, target.ConcurrencyStamp, target.MaterialStamp,
                target.Title, "safe local edit", target.Direction, new(), f.Now), ct);
            await Assert.That(edited.Failure).IsNull();
            await Assert.That(edited.Question!.Description).IsEqualTo("safe local edit");
            await Assert.That(source.Description).IsEqualTo("secret body 0");
        });
    }

    [Test, Timeout(300_000)]
    public async Task Stale_or_unavailable_source_and_non_solo_material_are_rejected_without_creating_instances(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var source = new CtfChallenge { Id = Guid.NewGuid(), OwnerId = f.Owner.Id, Title = "CTF source", Direction = "Web", Definition = new CtfChallengeDefinition(), CreatedAt = f.Now, UpdatedAt = f.Now };
            f.Db.Add(source); await f.Db.SaveChangesAsync(ct);
            await Assert.That((await Pool(f.Db).DeployAsync(new(f.Competition.Id, f.Owner.Id, source.Id, source.ConcurrencyStamp, f.Now), ct)).Failure).IsEqualTo(LiveSoloPoolFailure.UnsupportedDefinition);
            var local = await Pool(f.Db).CreateAsync(new(f.Competition.Id, f.Owner.Id, "Local", "local", "Web", new(), f.Now), ct);
            await Assert.That((await Pool(f.Db).DeployAsync(new(f.Competition.Id, f.Owner.Id, local.Question!.ChallengeId, local.Question.MaterialStamp, f.Now), ct)).Failure).IsEqualTo(LiveSoloPoolFailure.NotFound);
            await Assert.That((await Pool(f.Db).EditAsync(new(f.Competition.Id, f.Entries[0].Id, f.Owner.Id, Guid.NewGuid(), f.Templates[0].ConcurrencyStamp,
                "invalid", "invalid", "Web", new(), f.Now), ct)).Failure).IsEqualTo(LiveSoloPoolFailure.Conflict);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Failed_commit_rolls_back_material_identity_definitions_and_flags(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var failing = new FailCommit();
            await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(f.Options).AddInterceptors(failing).Options);
            failing.Armed = true;
            await Assert.That(async () => await Pool(db).EditAsync(new(f.Competition.Id, f.Entries[0].Id, f.Owner.Id,
                f.Entries[0].ConcurrencyStamp, f.Templates[0].ConcurrencyStamp, "new title", "new content", "Web", new(), f.Now), ct)).Throws<InvalidOperationException>();
            await Assert.That(await f.Db.Set<LiveSoloQuestionMaterial>().CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.Challenges.CountAsync(ct)).IsEqualTo(2);
            await Assert.That(await f.Db.ChallengeFlags.CountAsync(ct)).IsEqualTo(2);
            await Assert.That((await f.Db.CompetitionChallenges.AsNoTracking().SingleAsync(x => x.Id == f.Entries[0].Id, ct)).ChallengeId).IsEqualTo(f.Templates[0].Id);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Flag_only_edits_detach_atomically_and_never_change_the_bank_flag(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var pool = Pool(f.Db);
            var original = (await pool.ListAsync(f.Competition.Id, f.Owner.Id, ct))!.First(x => x.Id == f.Entries[0].Id);
            var edited = await pool.EditAsync(new(f.Competition.Id, original.Id, f.Owner.Id, original.ConcurrencyStamp, original.MaterialStamp,
                original.Title, original.Description, original.Direction, new(), f.Now, [new("flag{local}", ChallengeFlagMatchKind.Exact)]), ct);
            await Assert.That(edited.Failure).IsNull();
            await Assert.That(edited.Question!.Ownership).IsEqualTo(LiveSoloMaterialOwnership.CompetitionOwned);
            await Assert.That(edited.Question.Flags.Single().Flag).IsEqualTo("flag{local}");
            await Assert.That((await f.Db.ChallengeFlags.SingleAsync(x => x.ChallengeId == original.ChallengeId, ct)).Flag).IsEqualTo("flag{first}");
            var stale = await pool.EditAsync(new(f.Competition.Id, original.Id, f.Owner.Id, original.ConcurrencyStamp, original.MaterialStamp,
                original.Title, original.Description, original.Direction, new(), f.Now, []), ct);
            await Assert.That(stale.Failure).IsEqualTo(LiveSoloPoolFailure.Conflict);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Removing_an_attachment_detaches_keeps_file_reference_and_rejects_foreign_ids(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var file = new NoCTF.Domain.Storage.StoredFile { Id = Guid.NewGuid(), ObjectKey = "synthetic/pool.txt", FileName = "pool.txt", ByteLength = 4, Sha256 = new byte[32], CreatedAt = f.Now };
            var attachment = new ChallengeAttachment { Id = Guid.NewGuid(), ChallengeId = f.Templates[0].Id, File = file, CreatedAt = f.Now };
            f.Templates[0].Attachments.Add(attachment); f.Db.Add(attachment); await f.Db.SaveChangesAsync(ct);
            var pool = Pool(f.Db);
            var original = (await pool.ListAsync(f.Competition.Id, f.Owner.Id, ct))!.First(x => x.Id == f.Entries[0].Id);
            var command = new EditLiveSoloPoolQuestion(f.Competition.Id, original.Id, f.Owner.Id, original.ConcurrencyStamp, original.MaterialStamp,
                original.Title, original.Description, original.Direction, new(), f.Now, RemovedAttachmentIds: [Guid.NewGuid()]);
            await Assert.That((await pool.EditAsync(command, ct)).Failure).IsEqualTo(LiveSoloPoolFailure.InvalidContent);
            var edited = await pool.EditAsync(command with { RemovedAttachmentIds = [attachment.Id] }, ct);
            await Assert.That(edited.Failure).IsNull();
            await Assert.That(edited.Question!.Attachments.Count).IsEqualTo(0);
            await Assert.That(await f.Db.Set<ChallengeAttachment>().AnyAsync(x => x.Id == attachment.Id && x.DeletedAt == null, ct)).IsTrue();
            await Assert.That(await f.Db.Files.AnyAsync(x => x.Id == file.Id, ct)).IsTrue();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Concurrent_first_edits_create_only_one_independent_material_and_reject_the_loser(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var question = (await Pool(f.Db).ListAsync(f.Competition.Id, f.Owner.Id, ct))!.First(x => x.Id == f.Entries[0].Id);
            var command = new EditLiveSoloPoolQuestion(f.Competition.Id, question.Id, f.Owner.Id, question.ConcurrencyStamp, question.MaterialStamp,
                question.Title, "first edit", question.Direction, new(), f.Now);
            async Task<LiveSoloPoolResult> Edit(string content)
            {
                await using var db = new NoCtfDbContext(f.Options);
                return await Pool(db).EditAsync(command with { Description = content, Definition = new() }, ct);
            }
            var results = await Task.WhenAll(Edit("first"), Edit("second"));
            await Assert.That(results.Count(x => x.Failure is null)).IsEqualTo(1);
            await Assert.That(results.Count(x => x.Failure == LiveSoloPoolFailure.Conflict)).IsEqualTo(1);
            await Assert.That(await f.Db.Set<LiveSoloQuestionMaterial>().CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await f.Db.Challenges.CountAsync(ct)).IsEqualTo(3);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Added_immutable_file_detaches_and_becomes_a_competition_only_attachment(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var file = new NoCTF.Domain.Storage.StoredFile { Id = Guid.NewGuid(), ObjectKey = "synthetic/new.txt", FileName = "new.txt", ByteLength = 4, Sha256 = new byte[32], CreatedAt = f.Now };
            f.Db.Add(file); await f.Db.SaveChangesAsync(ct);
            var pool = Pool(f.Db); var question = (await pool.ListAsync(f.Competition.Id, f.Owner.Id, ct))!.First(x => x.Id == f.Entries[0].Id);
            var result = await pool.EditAsync(new(f.Competition.Id, question.Id, f.Owner.Id, question.ConcurrencyStamp, question.MaterialStamp,
                question.Title, question.Description, question.Direction, new(), f.Now, AddedAttachments: [new(file.Id)]), ct);
            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.Question!.Attachments.Single().FileName).IsEqualTo("new.txt");
            await Assert.That(result.Question.Ownership).IsEqualTo(LiveSoloMaterialOwnership.CompetitionOwned);
            await Assert.That(await f.Db.Set<ChallengeAttachment>().AnyAsync(x => x.ChallengeId == question.ChallengeId, ct)).IsFalse();
        });
    }

    private sealed class FailCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { if (Armed) { Armed = false; throw new InvalidOperationException("Injected material commit failure"); } return ValueTask.FromResult(result); }
    }
}
