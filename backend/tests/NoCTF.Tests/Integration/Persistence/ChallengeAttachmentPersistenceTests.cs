using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Challenges.Attachments;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeAttachmentPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Add_inserts_attachment_metadata_and_advances_template_revision(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_challenge_attachment")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var administratorId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var attachmentId = Guid.CreateVersion7();

            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = administratorId,
                UserName = "administrator",
                NormalizedUserName = "ADMINISTRATOR",
                Email = "administrator@example.test",
                NormalizedEmail = "ADMINISTRATOR@EXAMPLE.TEST",
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
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            var store = new ChallengeAttachmentStore(db);

            var added = await store.AddAsync(
                challengeId,
                administratorId,
                true,
                attachmentId,
                new StoredObject(
                    $"challenges/{challengeId:N}/attachments/{attachmentId:N}",
                    "attachment.txt",
                    "text/plain",
                    7,
                    new string('0', 64)),
                now,
                cancellationToken);

            await Assert.That(added).IsTrue();
            db.ChangeTracker.Clear();
            var attachment = await db.Set<ChallengeAttachment>()
                .SingleAsync(item => item.Id == attachmentId, cancellationToken);
            await Assert.That(attachment.ChallengeId).IsEqualTo(challengeId);
            await Assert.That(attachment.Length).IsEqualTo(7);
            var challenge = await db.Challenges.SingleAsync(
                item => item.Id == challengeId,
                cancellationToken);
            await Assert.That(challenge.Revision).IsEqualTo(1);
        });
    }
}
