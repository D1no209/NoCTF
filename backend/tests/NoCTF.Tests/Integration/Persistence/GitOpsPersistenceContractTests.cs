using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges.Attachments;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Challenges.Hints;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class GitOpsPersistenceContractTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Stable_ids_deleted_reads_and_restores_converge(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_gitops")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var administratorId = Guid.CreateVersion7(now);
            db.Users.Add(HumanAdministrator(administratorId, now));
            await db.SaveChangesAsync(cancellationToken);

            var passwordHasher = new PasswordHasher<User>();
            var botId = Guid.CreateVersion7(now.AddMilliseconds(1));
            var bot = new User
            {
                Id = botId,
                UserName = "repository-bot",
                NormalizedUserName = "REPOSITORY-BOT",
                Email = BotIdentity.DummyEmail(botId),
                Kind = UserKind.Bot,
                Role = UserRole.Organizer,
                CreatedAt = now,
                UpdatedAt = now
            };
            bot.PasswordHash = passwordHasher.HashPassword(
                bot,
                Guid.NewGuid().ToString("N"));
            db.Users.Add(bot);
            await db.SaveChangesAsync(cancellationToken);

            await Assert.That(await new AuthenticationStore(db, passwordHasher)
                .FindByLoginAsync("repository-bot", cancellationToken)).IsNull();
            await Assert.That(bot.TokenVersion).IsEqualTo(0);

            var competitionId = Guid.CreateVersion7(now.AddMilliseconds(2));
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = administratorId,
                ManagerIds = [botId],
                Title = "GitOps contract",
                Mode = GameMode.Ctf,
                ConfigurationJson =
                    """{"schemaVersion":2,"defaultScoreCurve":{"initialPoints":500,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}""",
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(1),
                EndAt = now.AddHours(2),
                Status = CompetitionStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);

            var challengeId = Guid.CreateVersion7(now.AddMilliseconds(3));
            var challengeBank = new ChallengeBankStore(db);
            var challenge = await challengeBank.CreateAsync(
                new(
                    challengeId,
                    botId,
                    GameMode.Ctf,
                    ChallengeVisibility.Private,
                    "Stable challenge",
                    "Statement",
                    "Web",
                    """{"schemaVersion":1}""",
                    now),
                cancellationToken);
            await Assert.That(challenge.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(challenge.Template!.Id).IsEqualTo(challengeId);
            await Assert.That((await challengeBank.ListAsync(
                botId,
                false,
                false,
                cancellationToken)).Select(item => item.Id))
                .IsEquivalentTo([challengeId]);

            var competitionChallengeId = Guid.CreateVersion7(now.AddMilliseconds(4));
            var competitionChallenges = new ChallengeManagementStore(
                db,
                Substitute.For<ITransactionalMessageOutbox>(),
                new ChallengeRuntimeTemplateCatalog());
            var linked = await competitionChallenges.CreateAsync(
                new(
                    competitionChallengeId,
                    competitionId,
                    challengeId,
                    1,
                    now,
                    "Finals Web"),
                """{"schemaVersion":2}""",
                cancellationToken);
            await Assert.That(linked.Challenge!.Id).IsEqualTo(competitionChallengeId);
            await Assert.That(linked.Challenge.Title).IsEqualTo("Finals Web");
            await Assert.That(linked.Challenge.CustomTitle).IsEqualTo("Finals Web");
            await Assert.That(linked.Challenge.HasRuntime).IsFalse();
            await Assert.That((await challengeBank.FindAsync(
                challengeId,
                botId,
                false,
                false,
                cancellationToken))!.ActiveCompetitionReferenceCount).IsEqualTo(1);

            var attachmentId = Guid.CreateVersion7(now.AddMilliseconds(5));
            var attachmentFileId = Guid.CreateVersion7(now.AddMilliseconds(6));
            db.Files.Add(new StoredFile
            {
                Id = attachmentFileId,
                ObjectKey = $"attachments/{attachmentId:N}",
                FileName = "handout.txt",
                ContentType = "text/plain",
                ByteLength = 7,
                Sha256 = new byte[32],
                CreatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            var attachmentStore = new ChallengeAttachmentStore(db);
            await Assert.That(await attachmentStore.AddAsync(
                challengeId,
                botId,
                false,
                attachmentId,
                attachmentFileId,
                now,
                cancellationToken)).IsEqualTo(AddChallengeAttachmentState.Added);
            await Assert.That(await attachmentStore.DeleteAsync(
                challengeId,
                attachmentId,
                botId,
                false,
                now,
                cancellationToken)).IsTrue();
            await Assert.That((await attachmentStore.ListAdminAsync(
                challengeId,
                botId,
                false,
                true,
                cancellationToken))!.Items.Single().DeletedAt).IsNotNull();
            await Assert.That(await attachmentStore.RestoreAsync(
                challengeId,
                attachmentId,
                botId,
                false,
                now,
                cancellationToken)).IsTrue();

            var flagId = Guid.CreateVersion7(now.AddMilliseconds(6));
            var flagStore = new ChallengeFlagManagementStore(db);
            var flagScope = ChallengeFlagScope.Template(challengeId);
            var flag = await flagStore.SaveAsync(
                new(
                    Scope: flagScope,
                    FlagId: flagId,
                    IsCreate: true,
                    TeamId: null,
                    Flag: "flag{gitops}",
                    SpecificationKind: null,
                    SpecificationId: null,
                    ValidStart: null,
                    ValidUntil: null,
                    Now: now),
                botId,
                false,
                cancellationToken);
            await Assert.That(flag.Flag!.Id).IsEqualTo(flagId);
            await Assert.That(await flagStore.DeleteAsync(
                flagScope,
                flagId,
                botId,
                false,
                now,
                cancellationToken)).IsEqualTo(ChallengeFlagMutationState.Updated);
            await Assert.That((await flagStore.ListAsync(
                flagScope,
                botId,
                false,
                true,
                cancellationToken))!.Single().DeletedAt).IsNotNull();
            await Assert.That(await flagStore.RestoreAsync(
                flagScope,
                flagId,
                botId,
                false,
                now,
                cancellationToken)).IsEqualTo(ChallengeFlagMutationState.Updated);

            var hintId = Guid.CreateVersion7(now.AddMilliseconds(7));
            var hintStore = new ChallengeHintStore(
                db,
                Substitute.For<ITransactionalMessageOutbox>());
            var hint = await hintStore.SaveAsync(
                new(
                    CompetitionId: competitionId,
                    CompetitionChallengeId: competitionChallengeId,
                    HintId: hintId,
                    IsCreate: true,
                    Content: "Read the source.",
                    Cost: 10,
                    PublishedAt: null,
                    Now: now),
                cancellationToken);
            await Assert.That(hint.Hint!.Id).IsEqualTo(hintId);
            await Assert.That(await hintStore.DeleteAsync(
                competitionId,
                competitionChallengeId,
                hintId,
                now,
                cancellationToken)).IsTrue();
            await Assert.That((await hintStore.ListAsync(
                competitionId,
                competitionChallengeId,
                true,
                cancellationToken))!.Single().DeletedAt).IsNotNull();
            await Assert.That(await hintStore.RestoreAsync(
                competitionId,
                competitionChallengeId,
                hintId,
                now,
                cancellationToken)).IsTrue();

            var beforeTitleReset = (await competitionChallenges.ListAsync(
                competitionId,
                true,
                false,
                cancellationToken)).Single();
            var restoredTemplateTitle = await competitionChallenges.UpdateAsync(
                new(
                    competitionId,
                    competitionChallengeId,
                    beforeTitleReset.Order,
                    beforeTitleReset.IsPublished,
                    now.AddSeconds(1),
                    null),
                cancellationToken);
            await Assert.That(restoredTemplateTitle.Challenge!.Title).IsEqualTo("Stable challenge");
            await Assert.That(restoredTemplateTitle.Challenge.CustomTitle).IsNull();

            await Assert.That(await competitionChallenges.SoftDeleteAsync(
                competitionId,
                competitionChallengeId,
                now,
                cancellationToken)).IsNull();
            var deletedCompetitionChallenge = (await competitionChallenges.ListAsync(
                competitionId,
                true,
                true,
                cancellationToken)).Single();
            await Assert.That(deletedCompetitionChallenge.DeletedAt).IsNotNull();
            await Assert.That((await challengeBank.FindAsync(
                challengeId,
                botId,
                false,
                false,
                cancellationToken))!.ActiveCompetitionReferenceCount).IsEqualTo(0);
            await Assert.That(await competitionChallenges.RestoreAsync(
                competitionId,
                competitionChallengeId,
                now,
                cancellationToken)).IsNull();

            var restoredCompetitionChallenge = (await competitionChallenges.ListAsync(
                competitionId,
                true,
                true,
                cancellationToken)).Single();
            await Assert.That(await competitionChallenges.SoftDeleteAsync(
                competitionId,
                competitionChallengeId,
                now,
                cancellationToken)).IsNull();
            await Assert.That(await challengeBank.SoftDeleteAsync(
                challengeId,
                botId,
                false,
                now,
                cancellationToken)).IsNull();
            await Assert.That(await challengeBank.ListAsync(
                botId,
                false,
                false,
                cancellationToken)).IsEmpty();
            await Assert.That((await challengeBank.ListAsync(
                botId,
                false,
                true,
                cancellationToken)).Single().DeletedAt).IsNotNull();
            await Assert.That(await challengeBank.FindAsync(
                challengeId,
                botId,
                false,
                false,
                cancellationToken)).IsNull();
            await Assert.That((await challengeBank.FindAsync(
                challengeId,
                botId,
                false,
                true,
                cancellationToken))!.DeletedAt).IsNotNull();
        });
    }

    private static User HumanAdministrator(Guid id, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = "administrator",
            NormalizedUserName = "ADMINISTRATOR",
            Email = "administrator@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.Administrator,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
}
