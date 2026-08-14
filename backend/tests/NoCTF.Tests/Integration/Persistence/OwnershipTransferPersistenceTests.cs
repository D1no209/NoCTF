using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Challenges.Attachments;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Persistence;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class OwnershipTransferPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Transfers_preserve_previous_owners_as_managers(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_ownership_transfer")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var previousOwnerId = Guid.CreateVersion7();
            var newOwnerId = Guid.CreateVersion7();
            var alternateOwnerId = Guid.CreateVersion7();
            var existingManagerId = Guid.CreateVersion7();
            db.Users.AddRange(
                Organizer(previousOwnerId, "previous-owner", now),
                Organizer(newOwnerId, "new-owner", now),
                Organizer(alternateOwnerId, "alternate-owner", now),
                Organizer(existingManagerId, "existing-manager", now));

            var competitionId = Guid.CreateVersion7();
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = previousOwnerId,
                ManagerIds = [existingManagerId],
                ObserverIds = [newOwnerId],
                Title = "Ownership transfer competition",
                Mode = GameMode.Ctf,
                ConfigurationJson = """{"schemaVersion":1}""",
                ConfigurationUpdatedAt = now,
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(1),
                EndAt = now.AddHours(2),
                Status = CompetitionStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            });

            var challengeId = Guid.CreateVersion7();
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = previousOwnerId,
                ManagerIds = [newOwnerId, existingManagerId],
                Mode = GameMode.Ctf,
                Visibility = ChallengeVisibility.Private,
                Title = "Ownership transfer challenge",
                Direction = "Web",
                DefinitionJson = """{"schemaVersion":1}""",
                Revision = 4,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);

            var transferredAt = now.AddMinutes(1);
            var competition = await new AdminCompetitionStore(db).TransferOwnerAsync(
                competitionId,
                previousOwnerId,
                false,
                newOwnerId,
                transferredAt,
                cancellationToken);
            var challenge = await new ChallengeBankStore(db).TransferOwnerAsync(
                challengeId,
                previousOwnerId,
                false,
                newOwnerId,
                4,
                transferredAt,
                cancellationToken);

            await Assert.That(competition.State)
                .IsEqualTo(CompetitionOwnerTransferState.Transferred);
            await Assert.That(challenge.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(challenge.Template!.Revision).IsEqualTo(5);
            await Assert.That((await new ChallengeBankStore(db).TransferOwnerAsync(
                challengeId,
                newOwnerId,
                false,
                Guid.NewGuid(),
                5,
                transferredAt.AddSeconds(1),
                cancellationToken)).State)
                .IsEqualTo(ChallengeTemplateWriteState.UserNotFound);

            db.ChangeTracker.Clear();
            var persistedCompetition = await db.Competitions
                .AsNoTracking()
                .SingleAsync(item => item.Id == competitionId, cancellationToken);
            var persistedChallenge = await db.Challenges
                .AsNoTracking()
                .SingleAsync(item => item.Id == challengeId, cancellationToken);

            await Assert.That(persistedCompetition.OwnerId).IsEqualTo(newOwnerId);
            await Assert.That(persistedCompetition.ManagerIds)
                .IsEquivalentTo([previousOwnerId, existingManagerId]);
            await Assert.That(persistedCompetition.ManagerIds.Count(id => id == previousOwnerId))
                .IsEqualTo(1);
            await Assert.That(persistedCompetition.ObserverIds.Contains(newOwnerId)).IsFalse();

            await Assert.That(persistedChallenge.OwnerId).IsEqualTo(newOwnerId);
            await Assert.That(persistedChallenge.ManagerIds)
                .IsEquivalentTo([previousOwnerId, existingManagerId]);
            await Assert.That(persistedChallenge.ManagerIds.Count(id => id == previousOwnerId))
                .IsEqualTo(1);
            await Assert.That(persistedChallenge.Revision).IsEqualTo(5);

            var concurrentChallengeId = Guid.CreateVersion7();
            db.Challenges.Add(new Challenge
            {
                Id = concurrentChallengeId,
                OwnerId = previousOwnerId,
                Mode = GameMode.Ctf,
                Visibility = ChallengeVisibility.Private,
                Title = "Concurrent ownership transfer",
                Direction = "Web",
                DefinitionJson = """{"schemaVersion":1}""",
                Revision = 7,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION noctf_delay_challenge_owner_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.owner_id IS DISTINCT FROM OLD.owner_id THEN
                        PERFORM pg_sleep(1);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """,
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER noctf_delay_challenge_owner_update
                BEFORE UPDATE ON challenges
                FOR EACH ROW
                EXECUTE FUNCTION noctf_delay_challenge_owner_update();
                """,
                cancellationToken);

            await using var firstTransferDb = new NoCtfDbContext(options);
            await using var secondTransferDb = new NoCtfDbContext(options);
            var firstTransferTask = new ChallengeBankStore(firstTransferDb).TransferOwnerAsync(
                concurrentChallengeId,
                previousOwnerId,
                true,
                newOwnerId,
                7,
                transferredAt,
                cancellationToken);
            await WaitForPostgresSleepAsync(db, cancellationToken);
            var secondTransferTask = new ChallengeBankStore(secondTransferDb).TransferOwnerAsync(
                concurrentChallengeId,
                previousOwnerId,
                true,
                alternateOwnerId,
                7,
                transferredAt,
                cancellationToken);
            var concurrentTransfers = await Task.WhenAll(
                firstTransferTask,
                secondTransferTask);

            await Assert.That(concurrentTransfers.Count(
                    result => result.State == ChallengeTemplateWriteState.Succeeded))
                .IsEqualTo(1);
            db.ChangeTracker.Clear();
            var concurrentChallenge = await db.Challenges
                .AsNoTracking()
                .SingleAsync(item => item.Id == concurrentChallengeId, cancellationToken);
            await Assert.That(
                new[] { newOwnerId, alternateOwnerId }.Contains(concurrentChallenge.OwnerId))
                .IsTrue();
            await Assert.That(concurrentChallenge.ManagerIds)
                .IsEquivalentTo([previousOwnerId]);
            await Assert.That(concurrentChallenge.Revision).IsEqualTo(8);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_competition_transfers_return_one_explicit_conflict(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_transfer_concurrency")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var previousOwnerId = Guid.CreateVersion7();
            var firstNewOwnerId = Guid.CreateVersion7();
            var secondNewOwnerId = Guid.CreateVersion7();
            var existingManagerId = Guid.CreateVersion7();
            db.Users.AddRange(
                Organizer(previousOwnerId, "competition-previous-owner", now),
                Organizer(firstNewOwnerId, "competition-first-new-owner", now),
                Organizer(secondNewOwnerId, "competition-second-new-owner", now),
                Organizer(existingManagerId, "competition-existing-manager", now));

            var competitionId = Guid.CreateVersion7();
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = previousOwnerId,
                ManagerIds = [existingManagerId],
                JudgeIds = [firstNewOwnerId],
                ObserverIds = [secondNewOwnerId],
                Title = "Concurrent competition ownership transfer",
                Mode = GameMode.Ctf,
                ConfigurationJson = """{"schemaVersion":1}""",
                ConfigurationUpdatedAt = now,
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(1),
                EndAt = now.AddHours(2),
                Status = CompetitionStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION noctf_delay_competition_owner_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.owner_id IS DISTINCT FROM OLD.owner_id THEN
                        PERFORM pg_sleep(1);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """,
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER noctf_delay_competition_owner_update
                BEFORE UPDATE ON competitions
                FOR EACH ROW
                EXECUTE FUNCTION noctf_delay_competition_owner_update();
                """,
                cancellationToken);

            await using var firstTransferDb = new NoCtfDbContext(options);
            await using var secondTransferDb = new NoCtfDbContext(options);
            var firstTransferTask = new AdminCompetitionStore(firstTransferDb).TransferOwnerAsync(
                competitionId,
                previousOwnerId,
                true,
                firstNewOwnerId,
                now.AddMinutes(1),
                cancellationToken);
            await WaitForPostgresSleepAsync(db, cancellationToken);
            var secondTransferTask = new AdminCompetitionStore(secondTransferDb).TransferOwnerAsync(
                competitionId,
                previousOwnerId,
                true,
                secondNewOwnerId,
                now.AddMinutes(1),
                cancellationToken);
            var transfers = await Task.WhenAll(firstTransferTask, secondTransferTask);

            await Assert.That(transfers.Count(
                    result => result.State == CompetitionOwnerTransferState.Transferred))
                .IsEqualTo(1);
            await Assert.That(transfers.Count(
                    result => result.State == CompetitionOwnerTransferState.RevisionConflict))
                .IsEqualTo(1);
            db.ChangeTracker.Clear();
            var persisted = await db.Competitions
                .AsNoTracking()
                .SingleAsync(item => item.Id == competitionId, cancellationToken);
            await Assert.That(new[] { firstNewOwnerId, secondNewOwnerId })
                .Contains(persisted.OwnerId);
            await Assert.That(persisted.ManagerIds)
                .IsEquivalentTo([previousOwnerId, existingManagerId]);
            await Assert.That(persisted.ManagerIds.Contains(persisted.OwnerId)).IsFalse();
            await Assert.That(persisted.JudgeIds.Contains(persisted.OwnerId)).IsFalse();
            await Assert.That(persisted.ObserverIds.Contains(persisted.OwnerId)).IsFalse();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Template_delete_and_reference_creation_return_an_explicit_conflict(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_challenge_reference_concurrency")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            db.Users.Add(Organizer(ownerId, "reference-owner", now));
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = ownerId,
                Title = "Reference serialization competition",
                Mode = GameMode.Ctf,
                ConfigurationJson = """{"schemaVersion":1}""",
                ConfigurationUpdatedAt = now,
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(1),
                EndAt = now.AddHours(2),
                Status = CompetitionStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                Mode = GameMode.Ctf,
                Visibility = ChallengeVisibility.Private,
                Title = "Reference serialization challenge",
                Direction = "Web",
                DefinitionJson = """{"schemaVersion":1}""",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION noctf_delay_challenge_delete()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.deleted_at IS DISTINCT FROM OLD.deleted_at THEN
                        PERFORM pg_sleep(1);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """,
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER noctf_delay_challenge_delete
                BEFORE UPDATE ON challenges
                FOR EACH ROW
                EXECUTE FUNCTION noctf_delay_challenge_delete();
                """,
                cancellationToken);

            await using var deleteDb = new NoCtfDbContext(options);
            await using var createDb = new NoCtfDbContext(options);
            var deleteTask = new ChallengeBankStore(deleteDb).SoftDeleteAsync(
                challengeId,
                ownerId,
                true,
                now.AddMinutes(1),
                cancellationToken);
            await WaitForPostgresSleepAsync(db, cancellationToken);
            var createTask = new ChallengeManagementStore(
                createDb,
                Substitute.For<ITransactionalMessageOutbox>(),
                new ChallengeRuntimeTemplateCatalog()).CreateAsync(
                new CreateCompetitionChallengeCommand(
                    Guid.CreateVersion7(),
                    competitionId,
                    challengeId,
                    500,
                    1,
                    now.AddMinutes(1)),
                """{"schemaVersion":1}""",
                cancellationToken);
            await Task.WhenAll(deleteTask, createTask);

            await Assert.That(await deleteTask).IsNull();
            await Assert.That((await createTask).Failure)
                .IsEqualTo(ChallengeMutationFailure.RevisionConflict);

            db.ChangeTracker.Clear();
            var templateDeletedAt = await db.Challenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.Id == challengeId)
                .Select(item => item.DeletedAt)
                .SingleAsync(cancellationToken);
            var hasActiveReference = await db.CompetitionChallenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(
                    item => item.ChallengeId == challengeId && item.DeletedAt == null,
                    cancellationToken);
            await Assert.That(templateDeletedAt is not null && hasActiveReference).IsFalse();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Attachment_revision_and_owner_transfer_do_not_lose_an_increment(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_attachment_revision_concurrency")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var previousOwnerId = Guid.CreateVersion7();
            var newOwnerId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var attachmentId = Guid.CreateVersion7();
            db.Users.AddRange(
                Organizer(previousOwnerId, "attachment-previous-owner", now),
                Organizer(newOwnerId, "attachment-new-owner", now));
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = previousOwnerId,
                Mode = GameMode.Ctf,
                Visibility = ChallengeVisibility.Private,
                Title = "Attachment revision serialization",
                Direction = "Web",
                DefinitionJson = """{"schemaVersion":1}""",
                Revision = 7,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION noctf_delay_challenge_revision_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.revision IS DISTINCT FROM OLD.revision THEN
                        PERFORM pg_sleep(1);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """,
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER noctf_delay_challenge_revision_update
                BEFORE UPDATE ON challenges
                FOR EACH ROW
                EXECUTE FUNCTION noctf_delay_challenge_revision_update();
                """,
                cancellationToken);

            var attachmentFileId = Guid.CreateVersion7(now.AddSeconds(1));
            db.Files.Add(new StoredFile
            {
                Id = attachmentFileId,
                ObjectKey = $"attachments/{attachmentId:N}",
                FileName = "serialized.txt",
                ContentType = "text/plain",
                ByteLength = 10,
                Sha256 = new byte[32],
                CreatedAt = now.AddMinutes(1)
            });
            await db.SaveChangesAsync(cancellationToken);
            await using var attachmentDb = new NoCtfDbContext(options);
            await using var transferDb = new NoCtfDbContext(options);
            var attachmentTask = new ChallengeAttachmentStore(attachmentDb).AddAsync(
                challengeId,
                previousOwnerId,
                true,
                attachmentId,
                attachmentFileId,
                now.AddMinutes(1),
                cancellationToken);
            await WaitForPostgresSleepAsync(db, cancellationToken);
            var transferTask = new ChallengeBankStore(transferDb).TransferOwnerAsync(
                challengeId,
                previousOwnerId,
                true,
                newOwnerId,
                7,
                now.AddMinutes(1),
                cancellationToken);
            await Task.WhenAll(attachmentTask, transferTask);

            await Assert.That(await attachmentTask)
                .IsEqualTo(AddChallengeAttachmentState.Added);
            await Assert.That((await transferTask).State)
                .IsEqualTo(ChallengeTemplateWriteState.RevisionConflict);
            db.ChangeTracker.Clear();
            var persisted = await db.Challenges
                .AsNoTracking()
                .SingleAsync(item => item.Id == challengeId, cancellationToken);
            await Assert.That(persisted.OwnerId).IsEqualTo(previousOwnerId);
            await Assert.That(persisted.Revision).IsEqualTo(8);
            await Assert.That(await db.Set<ChallengeAttachment>().AsNoTracking()
                    .AnyAsync(item => item.Id == attachmentId, cancellationToken))
                .IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Competition_restore_and_hard_delete_are_serialized(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_restore_hard_delete")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            db.Users.Add(Organizer(ownerId, "restore-hard-delete-owner", now));
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = ownerId,
                Title = "Restore and hard delete serialization",
                Mode = GameMode.Ctf,
                ConfigurationJson = """{"schemaVersion":1}""",
                ConfigurationUpdatedAt = now,
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(1),
                EndAt = now.AddHours(2),
                Status = CompetitionStatus.Draft,
                DeletedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION noctf_delay_competition_restore()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD.deleted_at IS NOT NULL AND NEW.deleted_at IS NULL THEN
                        PERFORM pg_sleep(1);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """,
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER noctf_delay_competition_restore
                BEFORE UPDATE ON competitions
                FOR EACH ROW
                EXECUTE FUNCTION noctf_delay_competition_restore();
                """,
                cancellationToken);

            await using var restoreDb = new NoCtfDbContext(options);
            await using var hardDeleteDb = new NoCtfDbContext(options);
            var restoreTask = new AdminCompetitionStore(restoreDb).RestoreAsync(
                competitionId,
                ownerId,
                false,
                now.AddMinutes(1),
                cancellationToken);
            await WaitForPostgresSleepAsync(db, cancellationToken);
            var hardDeleteTask = new AdminCompetitionStore(hardDeleteDb).HardDeleteAsync(
                competitionId,
                ownerId,
                false,
                cancellationToken);
            await Task.WhenAll(restoreTask, hardDeleteTask);

            await Assert.That((await restoreTask).State)
                .IsEqualTo(CompetitionRestoreState.Restored);
            await Assert.That((await hardDeleteTask).State)
                .IsEqualTo(CompetitionHardDeleteState.NotFound);
            db.ChangeTracker.Clear();
            var persisted = await db.Competitions.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(
                    competition => competition.Id == competitionId,
                    cancellationToken);
            await Assert.That(persisted.DeletedAt).IsNull();
        });
    }

    private static async Task WaitForPostgresSleepAsync(
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var sleepers = await db.Database.SqlQuery<int>(
                    $"""
                     SELECT count(*)::integer AS "Value"
                     FROM pg_stat_activity
                     WHERE datname = current_database() AND wait_event = 'PgSleep'
                     """)
                .SingleAsync(cancellationToken);
            if (sleepers > 0)
                return;
            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException("The PostgreSQL delay trigger did not enter pg_sleep.");
    }

    private static User Organizer(Guid id, string userName, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.Organizer,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
}
