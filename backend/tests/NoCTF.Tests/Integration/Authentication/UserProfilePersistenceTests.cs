using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.RefreshJwt;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
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
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
            var avatarFileId = Guid.CreateVersion7(now.AddMinutes(2));
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
                var initialProfile = await users.GetProfileAsync(userId, cancellationToken);
                await Assert.That(initialProfile).IsNotNull();
                await users.UpdateProfileAsync(
                    userId,
                    "Persistent profile",
                    now.AddMinutes(1),
                    cancellationToken);
                db.Files.Add(new StoredFile
                {
                    Id = avatarFileId,
                    ObjectKey = avatarObjectKey,
                    FileName = "avatar.webp",
                    ContentType = "image/webp",
                    ByteLength = 4,
                    Sha256 = new byte[32],
                    CreatedAt = now.AddMinutes(2)
                });
                await db.SaveChangesAsync(cancellationToken);
                await users.ReplaceAvatarAsync(
                    userId,
                    avatarFileId,
                    now.AddMinutes(2),
                    cancellationToken);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var profile = await new AuthenticationStore(db, hasher)
                    .GetProfileAsync(userId, cancellationToken);

                await Assert.That(profile).IsNotNull();
                await Assert.That(profile!.Description).IsEqualTo("Persistent profile");
                await Assert.That(profile.AvatarFileId).IsNotNull();
                var storedFile = await db.Files.SingleAsync(
                    file => file.Id == profile.AvatarFileId,
                    cancellationToken);
                await Assert.That(storedFile.ObjectKey).IsEqualTo(avatarObjectKey);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Password_change_is_atomic_and_invalidates_existing_access_and_refresh_versions(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_password_change")
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

            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            var users = new AuthenticationStore(db, hasher);
            await users.CreateAsync(
                userId,
                "PasswordOwner",
                "password-owner@example.test",
                "old-pass",
                true,
                now,
                cancellationToken);
            var before = await users.FindByIdAsync(userId, cancellationToken);

            var state = await users.ChangePasswordAsync(
                userId,
                "old-pass",
                "new-pass",
                now.AddMinutes(1),
                cancellationToken);
            var after = await users.FindByIdAsync(userId, cancellationToken);

            await Assert.That(state).IsEqualTo(ChangePasswordState.Changed);
            await Assert.That(after!.TokenVersion).IsEqualTo(before!.TokenVersion + 1);
            await Assert.That(await users.VerifyPasswordAsync(
                userId, "old-pass", cancellationToken)).IsFalse();
            await Assert.That(await users.VerifyPasswordAsync(
                userId, "new-pass", cancellationToken)).IsTrue();
            await Assert.That(await new AccessTokenVersionReader(db).IsCurrentAsync(
                userId, before.TokenVersion, cancellationToken)).IsFalse();

            var refresh = await new RefreshAccessToken(
                users,
                new StaleRefreshIssuer(new(userId, before.TokenVersion)))
                .ExecuteAsync("old-refresh", cancellationToken);
            await Assert.That(refresh.Succeeded).IsFalse();
            await Assert.That(refresh.FailureCode).IsEqualTo(RefreshAccessTokenFailureCode.RefreshInvalid);
        });
    }

    private sealed class StaleRefreshIssuer(RefreshTokenPrincipal principal) : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(
            AuthenticatedUser user,
            DateTimeOffset now,
            TimeSpan? lifetime = null) =>
            new("unused-access", now.AddMinutes(15));

        public IssuedRefreshToken IssueRefresh(AuthenticatedUser user) =>
            new("unused-refresh", DateTimeOffset.UtcNow.AddDays(30));

        public RefreshTokenPrincipal? ValidateRefresh(string token) => principal;
    }
}
