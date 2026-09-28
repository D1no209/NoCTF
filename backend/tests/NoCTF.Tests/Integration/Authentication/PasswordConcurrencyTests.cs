using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class PasswordConcurrencyTests
{
    [Test, Timeout(300_000)]
    public async Task Password_change_and_reset_share_atomic_credential_protection(CancellationToken ct)
    {
        await WithUser(async (options, user, hasher) => {
            const string token = "isolated-reset-token";
            await using (var setup = new NoCtfDbContext(options)) {
                setup.AccountTokens.Add(new AccountToken { Id = Guid.NewGuid(), UserId = user.Id, Kind = AccountTokenKind.PasswordReset,
                    TokenSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(token)), CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }); await setup.SaveChangesAsync(ct);
            }
            await using var changeDb = new NoCtfDbContext(options); await using var resetDb = new NoCtfDbContext(options);
            var change = new AuthenticationStore(changeDb, hasher).ChangePasswordAsync(user.Id, "old-password", "changed-password", DateTimeOffset.UtcNow, ct);
            var reset = new PasswordResetStore(resetDb, hasher, Substitute.For<IEmailVerificationConfigurationStore>(),
                Substitute.For<IEmailVerificationDeliveryConfigurationReader>(), new NoOpPostCommitMessagePublisher())
                .CompleteAsync(token, "reset-password", DateTimeOffset.UtcNow, ct);
            await Task.WhenAll(change, reset);
            await Assert.That((await change == ChangePasswordState.Changed ? 1 : 0) + (await reset == PasswordResetCompletionState.Reset ? 1 : 0)).IsEqualTo(1);
            await using var verify = new NoCtfDbContext(options);
            await Assert.That((await verify.Users.SingleAsync(ct)).TokenVersion).IsEqualTo(1);
        }, ct);
    }

    [Test, Timeout(300_000)]
    public async Task Identity_v2_password_hash_is_rejected(CancellationToken ct)
    {
        await WithUser(async (options, user, hasher) =>
        {
            var identityV2 = new PasswordHasher<User>(Options.Create(
                new PasswordHasherOptions
                {
                    CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
                }));
            await using (var setup = new NoCtfDbContext(options))
            {
                var entity = await setup.Users.SingleAsync(ct);
                entity.PasswordHash = identityV2.HashPassword(entity, "old-password");
                await setup.SaveChangesAsync(ct);
            }

            await using var verify = new NoCtfDbContext(options);
            await Assert.That(await new AuthenticationStore(verify, hasher)
                    .VerifyPasswordAsync(user.Id, "old-password", ct))
                .IsFalse();
        }, ct);
    }

    [Test, Timeout(300_000)]
    public async Task Independent_token_revocation_is_not_overwritten_by_password_change(CancellationToken ct)
    {
        await WithUser(async (options, user, hasher) => {
            await using var left = new NoCtfDbContext(options); await using var right = new NoCtfDbContext(options);
            await Task.WhenAll(new AuthenticationStore(left, hasher).IncrementTokenVersionAsync(user.Id, DateTimeOffset.UtcNow, ct),
                new AuthenticationStore(right, hasher).ChangePasswordAsync(user.Id, "old-password", "new-password", DateTimeOffset.UtcNow, ct));
            await using var verify = new NoCtfDbContext(options);
            await Assert.That((await verify.Users.SingleAsync(ct)).TokenVersion).IsEqualTo(2);
        }, ct);
    }

    private static async Task WithUser(Func<DbContextOptions<NoCtfDbContext>, User, PasswordHasher<User>, Task> test, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () => {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build(); await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var hasher = new PasswordHasher<User>(); var now = DateTimeOffset.UtcNow;
            var user = new User { Id = Guid.NewGuid(), UserName = "race", NormalizedUserName = "RACE", Email = "race@example.test", Kind = UserKind.Human,
                AccountStatus = UserAccountStatus.Active, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now };
            user.PasswordHash = hasher.HashPassword(user, "old-password");
            await using (var setup = new NoCtfDbContext(options)) { await setup.Database.EnsureCreatedAsync(ct); setup.Users.Add(user); await setup.SaveChangesAsync(ct); }
            await test(options, user, hasher);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Two_writers_with_the_same_old_password_only_one_can_commit(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var hasher = new PasswordHasher<User>();
            var user = new User { Id = Guid.NewGuid(), UserName = "race", NormalizedUserName = "RACE", Email = "race@example.test",
                Kind = UserKind.Human, AccountStatus = UserAccountStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            user.PasswordHash = hasher.HashPassword(user, "old-password");
            await using (var setup = new NoCtfDbContext(options)) { await setup.Database.EnsureCreatedAsync(ct); setup.Users.Add(user); await setup.SaveChangesAsync(ct); }
            await using var left = new NoCtfDbContext(options); await using var right = new NoCtfDbContext(options);
            // Both request scopes have observed the same credential before either update.
            await left.Users.SingleAsync(ct); await right.Users.SingleAsync(ct);
            var results = await Task.WhenAll(
                new AuthenticationStore(left, hasher).ChangePasswordAsync(user.Id, "old-password", "left-password", DateTimeOffset.UtcNow, ct),
                new AuthenticationStore(right, hasher).ChangePasswordAsync(user.Id, "old-password", "right-password", DateTimeOffset.UtcNow, ct));
            await Assert.That(results.Count(value => value == ChangePasswordState.Changed)).IsEqualTo(1);
            await using var verify = new NoCtfDbContext(options);
            await Assert.That((await verify.Users.SingleAsync(ct)).TokenVersion).IsEqualTo(1);
        });
    }
}
