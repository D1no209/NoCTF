using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Webhooks;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionWebhooks")]
public sealed class CompetitionWebhookPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Targets_are_unbounded_encrypted_and_rotated_without_losing_siblings(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_webhooks")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7(now);
            var competitionId = Guid.CreateVersion7(now.AddTicks(1));
            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync(cancellationToken);
                seed.Users.Add(new User
                {
                    Id = ownerId,
                    UserName = "webhook-owner",
                    NormalizedUserName = "WEBHOOK-OWNER",
                    Email = "webhook-owner@example.test",
                    PasswordHash = "test",
                    Role = UserRole.Organizer,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    Title = "Webhook persistence",
                    OwnerId = ownerId,
                    Mode = GameMode.Ctf,
                    Status = CompetitionStatus.Published,
                    ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf),
                    StartAt = now,
                    EndAt = now.AddHours(2),
                    FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await seed.SaveChangesAsync(cancellationToken);
            }

            var protector = new PlatformSecretProtector(Options.Create(
                new EmailVerificationProtectionOptions
                {
                    EncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32)
                        .Select(value => (byte)value).ToArray())
                }));
            Guid targetId;
            string originalSecret;
            await using (var createDb = new NoCtfDbContext(options))
            {
                var store = new CompetitionWebhookStore(
                    createDb,
                    protector,
                    NullCompetitionEventRecorder.Instance,
                    new NoOpTransactionalMessageOutbox());
                var result = await store.CreateAsync(new(
                    competitionId,
                    "Primary",
                    "https://hooks.example.test/noctf",
                    true,
                    now), cancellationToken);
                await Assert.That(result.Succeeded).IsTrue();
                await Assert.That(result.SigningSecret).StartsWith("whsec_");
                targetId = result.Target!.Id;
                originalSecret = result.SigningSecret!;
            }

            await using (var expandDb = new NoCtfDbContext(options))
            {
                var competition = await expandDb.Competitions.SingleAsync(
                    item => item.Id == competitionId,
                    cancellationToken);
                for (var index = 0; index < 1_000; index++)
                {
                    competition.WebhookConfiguration.Targets.Add(new CompetitionWebhookTarget
                    {
                        Id = Guid.CreateVersion7(now.AddTicks(index + 10)),
                        Name = $"Target {index}",
                        EndpointUrl = $"https://hooks-{index}.example.test/noctf",
                        CurrentSecretCiphertext = [1, 2, 3],
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                await expandDb.SaveChangesAsync(cancellationToken);
            }

            await using (var rotateDb = new NoCtfDbContext(options))
            {
                var store = new CompetitionWebhookStore(
                    rotateDb,
                    protector,
                    NullCompetitionEventRecorder.Instance,
                    new NoOpTransactionalMessageOutbox());
                var page = await store.ListAsync(
                    competitionId,
                    null,
                    null,
                    100,
                    cancellationToken);
                await Assert.That(page!.Items).Count().IsEqualTo(100);
                await Assert.That(page.HasMore).IsTrue();

                var rotated = await store.RotateSecretAsync(
                    competitionId,
                    targetId,
                    now.AddMinutes(1),
                    cancellationToken);
                await Assert.That(rotated.Succeeded).IsTrue();
                await Assert.That(rotated.SigningSecret).IsNotEqualTo(originalSecret);
                await Assert.That(rotated.Target!.PreviousSecretValidUntil)
                    .IsEqualTo(now.AddHours(24).AddMinutes(1));
            }

            await using (var verifyDb = new NoCtfDbContext(options))
            {
                var configuration = await verifyDb.Competitions.AsNoTracking()
                    .Where(item => item.Id == competitionId)
                    .Select(item => item.WebhookConfiguration)
                    .SingleAsync(cancellationToken);
                await Assert.That(configuration.Targets).Count().IsEqualTo(1_001);
                var serialized = JsonSerializer.Serialize(configuration);
                await Assert.That(serialized).DoesNotContain(originalSecret);
                var primary = configuration.Targets.Single(item => item.Id == targetId);
                var unprotected = protector.Unprotect(
                    primary.PreviousSecretCiphertext!,
                    PlatformSecretPurpose.CompetitionWebhookSecret,
                    competitionId,
                    targetId);
                await Assert.That(unprotected).IsEqualTo(originalSecret);
                await Assert.That(() => protector.Unprotect(
                        primary.PreviousSecretCiphertext!,
                        PlatformSecretPurpose.CompetitionWebhookSecret,
                        Guid.CreateVersion7(),
                        targetId))
                    .Throws<CryptographicException>();
            }
        });
    }
}
