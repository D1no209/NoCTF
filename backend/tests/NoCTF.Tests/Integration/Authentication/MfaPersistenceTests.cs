using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Authentication.Mfa;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using OtpNet;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
[NotInParallel]
public sealed class MfaPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Enrollment_and_concurrent_challenge_code_consumption_are_atomic(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = Container(); await postgres.StartAsync(ct);
        var options = Options(postgres); var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
        var userId = Guid.NewGuid(); var protector = Protector(); var crypto = new MfaCryptography();
        await using (var db = new NoCtfDbContext(options))
        {
            await db.Database.MigrateAsync(ct); db.Users.Add(User(userId, UserKind.Human)); await db.SaveChangesAsync(ct);
            var store = new MfaAuthenticationStore(db, crypto, protector, clock);
            var account = (await store.ReadAccountAsync(userId, ct))!;
            var (_, browser) = await store.CreateFlowAsync(new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Enrollment, ct);
            var begun = await store.BeginEnrollmentAsync(browser, ct);
            await Assert.That(begun.Succeeded).IsTrue();
            var enrolled = await store.ConfirmEnrollmentAsync(browser, Code(begun.Value!.Secret!, clock), ct);
            await Assert.That(enrolled.Succeeded).IsTrue();
            await Assert.That(enrolled.Value!.RecoveryCodes!.Count).IsEqualTo(10);
            await Assert.That(enrolled.Value.User.TokenVersion).IsEqualTo(1);
            await Assert.That((await store.ConfirmEnrollmentAsync(browser, Code(begun.Value.Secret!, clock), ct)).Succeeded).IsFalse();
        }
        clock.Advance(TimeSpan.FromSeconds(30));
        MfaBrowserCredential login; string secret;
        await using (var db = new NoCtfDbContext(options))
        {
            var store = new MfaAuthenticationStore(db, crypto, protector, clock); var account = (await store.ReadAccountAsync(userId, ct))!;
            (_, login) = await store.CreateFlowAsync(new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Login, ct);
            var credential = await db.UserTotpCredentials.SingleAsync(ct);
            secret = protector.Unprotect(credential.SecretCiphertext, PlatformSecretPurpose.TotpCredentialSecret, userId, credential.Id);
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = new NoCtfDbContext(options);
            return await new MfaAuthenticationStore(db, crypto, protector, clock).VerifyAsync(login, new(MfaVerificationMethod.Totp, Code(secret, clock)), ct);
        }));
        await Assert.That(results.Count(result => result.Succeeded)).IsEqualTo(1);
        await using (var db = new NoCtfDbContext(options))
        {
            var store = new MfaAuthenticationStore(db, crypto, protector, clock); var account = (await store.ReadAccountAsync(userId, ct))!;
            var (_, next) = await store.CreateFlowAsync(new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Login, ct);
            var replay = await store.VerifyAsync(next, new(MfaVerificationMethod.Totp, Code(secret, clock)), ct);
            await Assert.That(replay.Succeeded).IsFalse();
            await Assert.That(replay.FailureCode).IsEqualTo(MfaFailure.CodeAlreadyUsed);
        }
    });

    [Test, Timeout(300_000)]
    public Task Failure_budgets_survive_new_challenges_and_Bots_keep_context_free_access(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = Container(); await postgres.StartAsync(ct); var options = Options(postgres);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z")); var crypto = new MfaCryptography(); var protector = Protector();
        var userId = Guid.NewGuid(); var botId = Guid.NewGuid(); var secret = crypto.GenerateSecret(); var credentialId = Guid.NewGuid();
        await using var db = new NoCtfDbContext(options); await db.Database.MigrateAsync(ct);
        db.Users.AddRange(User(userId, UserKind.Human), User(botId, UserKind.Bot));
        db.UserTotpCredentials.Add(new() { Id = credentialId, UserId = userId, SecretCiphertext = protector.Protect(secret, PlatformSecretPurpose.TotpCredentialSecret, userId, credentialId), EnabledAt = clock.GetUtcNow(), RecoveryBatchId = Guid.NewGuid() });
        await db.SaveChangesAsync(ct); var store = new MfaAuthenticationStore(db, crypto, protector, clock);
        for (var flowNumber = 0; flowNumber < 2; flowNumber++)
        {
            var account = (await store.ReadAccountAsync(userId, ct))!;
            var (_, browser) = await store.CreateFlowAsync(new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Login, ct);
            for (var attempt = 0; attempt < 5; attempt++) await store.VerifyAsync(browser, new(MfaVerificationMethod.Totp, "invalid"), ct);
        }
        var latest = (await store.ReadAccountAsync(userId, ct))!;
        var (_, blocked) = await store.CreateFlowAsync(new(latest.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Login, ct);
        var rejected = await store.VerifyAsync(blocked, new(MfaVerificationMethod.Totp, Code(secret, clock)), ct);
        await Assert.That(rejected.FailureCode).IsEqualTo(MfaFailure.RateLimited);
        db.ChangeTracker.Clear(); var settings = await db.PlatformSettings.SingleAsync(ct); settings.MfaPolicy = MfaPolicy.RequireAllHumanUsers; settings.MfaPolicyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await Assert.That(await store.ValidateContextAsync(botId, 0, null, ct)).IsNull();
        await Assert.That(await store.ValidateContextAsync(userId, 0, null, ct)).IsEqualTo(MfaFailure.PrimaryAuthenticationRequired);
        await Assert.That((await store.ReadFlowAsync(blocked, ct)).FailureCode).IsEqualTo(MfaFailure.PolicyChanged);
    });

    private static PostgreSqlContainer Container() => new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
        .WithDatabase("mfa").WithUsername("postgres").WithPassword("postgres").Build();
    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) => new DbContextOptionsBuilder<NoCtfDbContext>()
        .UseNpgsql(postgres.GetConnectionString(), value => value.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
    private static PlatformSecretProtector Protector() => new(Microsoft.Extensions.Options.Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
    private static User User(Guid id, UserKind kind) => new() { Id = id, UserName = id.ToString("N"), NormalizedUserName = id.ToString("N").ToUpperInvariant(),
        Email = $"{id:N}@test.invalid", PasswordHash = "test", Kind = kind, Role = UserRole.Administrator, AccountStatus = UserAccountStatus.Active };
    private static string Code(string secret, FakeTimeProvider clock) => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(clock.GetUtcNow().UtcDateTime);
}
