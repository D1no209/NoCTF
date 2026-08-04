using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class UserProfilePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Description_and_avatar_metadata_survive_a_new_database_context(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_user_profile")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
            {
                IterationCount = 10_000
            }));
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);
            const string avatarObjectKey = "users/profile/avatar.webp";

            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.MigrateAsync(cancellationToken);
                var users = new AuthenticationStore(db, hasher);
                await users.CreateAsync(
                    userId,
                    "Player",
                    "player@example.test",
                    "eight888",
                    true,
                    now,
                    cancellationToken);
                await users.UpdateProfileAsync(
                    userId,
                    "Persistent profile",
                    now.AddMinutes(1),
                    cancellationToken);
                await users.ReplaceAvatarAsync(
                    userId,
                    avatarObjectKey,
                    now.AddMinutes(2),
                    cancellationToken);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var profile = await new AuthenticationStore(db, hasher)
                    .GetProfileAsync(userId, cancellationToken);

                await Assert.That(profile).IsNotNull();
                await Assert.That(profile!.Description).IsEqualTo("Persistent profile");
                await Assert.That(profile.AvatarObjectKey).IsEqualTo(avatarObjectKey);
            }
        });
    }
}
