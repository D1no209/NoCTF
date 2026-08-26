using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Challenges.Attachments;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeAttachmentPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Add_inserts_attachment_metadata_and_updates_template_timestamp(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_challenge_attachment")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var administratorId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var attachmentId = Guid.CreateVersion7();

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = administratorId,
                UserName = "administrator",
                NormalizedUserName = "ADMINISTRATOR",
                Email = "administrator@example.test",
                PasswordHash = "test",
                Role = UserRole.Administrator,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = administratorId,
                Title = "Attachment persistence",
                DefinitionJson = "{}",
                CreatedAt = now,
                UpdatedAt = now
            });
            var fileId = Guid.CreateVersion7();
            db.Files.Add(new StoredFile
            {
                Id = fileId,
                ObjectKey = $"attachments/{attachmentId:N}",
                FileName = "attachment.txt",
                ContentType = "text/plain",
                ByteLength = 7,
                Sha256 = new byte[32],
                CreatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            var store = new ChallengeAttachmentStore(db);

            var added = await store.AddAsync(
                challengeId,
                administratorId,
                true,
                attachmentId,
                fileId,
                now,
                cancellationToken);

            await Assert.That(added).IsEqualTo(AddChallengeAttachmentState.Added);
            db.ChangeTracker.Clear();
            var attachment = await db.Set<ChallengeAttachment>()
                .Include(item => item.File)
                .SingleAsync(item => item.Id == attachmentId, cancellationToken);
            await Assert.That(attachment.ChallengeId).IsEqualTo(challengeId);
            await Assert.That(attachment.Length).IsEqualTo(7);
            var challenge = await db.Challenges.SingleAsync(item => item.Id == challengeId, cancellationToken);
            await Assert.That(challenge.UpdatedAt).IsEqualTo(now);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Random_batch_persists_attachments_and_exact_flags_atomically(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_random_attachment_batch")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = Options(postgres.GetConnectionString());
            var now = DateTimeOffset.UtcNow;
            var administratorId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var attachmentIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
            var fileIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
            var exactFlags = new[] { "flag{batch-one}.zip", "flag{batch-two}.zip" };

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = administratorId,
                UserName = "batch-administrator",
                NormalizedUserName = "BATCH-ADMINISTRATOR",
                Email = "batch-administrator@example.test",
                PasswordHash = "test",
                Role = UserRole.Administrator,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = administratorId,
                Title = "Random batch",
                Mode = GameMode.Ctf,
                DefinitionJson = "{}",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Files.AddRange(fileIds.Select((id, index) => new StoredFile
            {
                Id = id,
                ObjectKey = $"attachments/{attachmentIds[index]:N}",
                FileName = "challenge.zip",
                ContentType = "application/zip",
                ByteLength = index + 1,
                Sha256 = new byte[32],
                CreatedAt = now
            }));
            await db.SaveChangesAsync(cancellationToken);

            var result = await new ChallengeAttachmentStore(db).AddRandomBatchAsync(
                challengeId,
                administratorId,
                true,
                "challenge.zip",
                attachmentIds.Select((id, index) => new RandomAttachmentBatchEntry(
                    id,
                    fileIds[index],
                    exactFlags[index],
                    now)).ToArray(),
                cancellationToken);

            await Assert.That(result).IsEqualTo(AddChallengeAttachmentState.Added);
            db.ChangeTracker.Clear();
            var storedAttachments = await db.Set<ChallengeAttachment>()
                .Include(attachment => attachment.File)
                .Where(attachment => attachment.ChallengeId == challengeId)
                .OrderBy(attachment => attachment.Id)
                .ToArrayAsync(cancellationToken);
            var storedFlags = await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.ChallengeId == challengeId)
                .OrderBy(flag => flag.Flag)
                .ToArrayAsync(cancellationToken);
            await Assert.That(storedAttachments.Length).IsEqualTo(2);
            await Assert.That(storedAttachments.All(attachment => attachment.File.FileName == "challenge.zip")).IsTrue();
            await Assert.That(storedFlags.Select(flag => flag.Flag).Order().ToArray())
                .IsEquivalentTo(exactFlags.Order().ToArray());
            await Assert.That(storedFlags.All(flag =>
                flag.MatchKind == ChallengeFlagMatchKind.Exact
                && flag.TeamId == null
                && flag.SpecificationKind == SpecificationKind.Attachment
                && attachmentIds.Contains(flag.SpecificationId!.Value))).IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Random_assignments_are_stable_unique_until_exhausted_and_concurrency_safe(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_random_attachment_assignment")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var connectionString = postgres.GetConnectionString();
            var options = Options(connectionString);
            var now = DateTimeOffset.UtcNow;
            var users = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
            var teams = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
            var competitionId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var attachments = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };

            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync(cancellationToken);
                seed.Users.AddRange(users.Select((id, index) => new User
                {
                    Id = id,
                    UserName = $"participant-{index}",
                    NormalizedUserName = $"PARTICIPANT-{index}",
                    Email = $"participant-{index}@example.test",
                    PasswordHash = "test",
                    CreatedAt = now,
                    UpdatedAt = now
                }));
                seed.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    OwnerId = users[0],
                    Title = "Random attachment assignment",
                    Mode = GameMode.Ctf,
                    Status = CompetitionStatus.Running,
                    ConfigurationJson = "{}",
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddHours(-1),
                    EndAt = now.AddHours(1),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.Teams.AddRange(teams.Select((id, index) => new Team
                {
                    Id = id,
                    CompetitionId = competitionId,
                    Name = $"team-{index}",
                    CaptainId = users[index],
                    MemberIds = [users[index]],
                    InvitationToken = $"{index:D2}123456789012345678901234567890",
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                }));
                seed.Challenges.Add(new Challenge
                {
                    Id = challengeId,
                    OwnerId = users[0],
                    Title = "Random delivery",
                    Mode = GameMode.Ctf,
                    DefinitionJson = "{}",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.CompetitionChallenges.Add(new CompetitionChallenge
                {
                    Id = competitionChallengeId,
                    CompetitionId = competitionId,
                    ChallengeId = challengeId,
                    Order = 1,
                    IsPublished = true,
                    RulesJson = "{\"schemaVersion\":1}",
                    UpdatedAt = now
                });
                foreach (var (attachmentId, index) in attachments.Select((id, index) => (id, index)))
                {
                    var fileId = Guid.CreateVersion7();
                    var flag = $"flag{{variant-{index}}}";
                    seed.Files.Add(new StoredFile
                    {
                        Id = fileId,
                        ObjectKey = $"attachments/{attachmentId:N}",
                        FileName = "challenge.zip",
                        ContentType = "application/zip",
                        ByteLength = 1,
                        Sha256 = new byte[32],
                        CreatedAt = now
                    });
                    seed.Set<ChallengeAttachment>().Add(new ChallengeAttachment
                    {
                        Id = attachmentId,
                        ChallengeId = challengeId,
                        FileId = fileId,
                        CreatedAt = now
                    });
                    seed.ChallengeFlags.Add(new ChallengeFlag
                    {
                        Id = Guid.CreateVersion7(),
                        ChallengeId = challengeId,
                        Flag = flag,
                        FlagSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash(flag),
                        MatchKind = ChallengeFlagMatchKind.Exact,
                        SpecificationKind = SpecificationKind.Attachment,
                        SpecificationId = attachmentId,
                        CreatedAt = now
                    });
                }
                await seed.SaveChangesAsync(cancellationToken);
            }

            async Task<Guid> DownloadAsync(Guid userId)
            {
                await using var db = new NoCtfDbContext(Options(connectionString));
                var result = await new ChallengeAttachmentStore(db).GetPlayerAsync(
                    competitionId,
                    competitionChallengeId,
                    null,
                    userId,
                    (_, _) => Task.FromResult(true),
                    cancellationToken);
                return result!.Metadata.Id;
            }

            await using (var unavailableDb = new NoCtfDbContext(Options(connectionString)))
            {
                var unavailable = await new ChallengeAttachmentStore(unavailableDb).GetPlayerAsync(
                    competitionId,
                    competitionChallengeId,
                    null,
                    users[0],
                    (_, _) => Task.FromResult(false),
                    cancellationToken);
                await Assert.That(unavailable).IsNull();
            }
            await using (var unavailableVerification = new NoCtfDbContext(Options(connectionString)))
            {
                await Assert.That(await unavailableVerification.ChallengeFlags.AsNoTracking()
                    .CountAsync(flag => flag.CompetitionChallengeId == competitionChallengeId, cancellationToken))
                    .IsEqualTo(0);
            }

            var sameTeam = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => DownloadAsync(users[0])));
            await Assert.That(sameTeam.Distinct().Count()).IsEqualTo(1);

            var laterTeams = await Task.WhenAll(DownloadAsync(users[1]), DownloadAsync(users[2]));
            var previouslyUnusedAttachment = attachments.Single(id => id != sameTeam[0]);
            await Assert.That(laterTeams).Contains(previouslyUnusedAttachment);
            await Assert.That(laterTeams.All(attachments.Contains)).IsTrue();

            await using var verification = new NoCtfDbContext(Options(connectionString));
            var assignments = await verification.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.CompetitionChallengeId == competitionChallengeId
                    && flag.TeamId != null
                    && flag.SpecificationKind == SpecificationKind.Attachment)
                .ToArrayAsync(cancellationToken);
            await Assert.That(assignments.Length).IsEqualTo(3);
            await Assert.That(assignments.Where(flag =>
                flag.TeamId == teams[0]).Select(flag => flag.SpecificationId).Distinct().Count())
                .IsEqualTo(1);
        });
    }

    private static DbContextOptions<NoCtfDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
}
