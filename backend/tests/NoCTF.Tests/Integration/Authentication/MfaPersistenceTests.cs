using NSubstitute;
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
    public Task Account_enrollment_requires_relogin_and_recovery_code_consumption_is_global(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = Container(); await postgres.StartAsync(ct); var options = Options(postgres);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z")); var crypto = new MfaCryptography(); var protector = Protector();
        var userId = Guid.NewGuid(); IReadOnlyList<string> recoveryCodes; MfaBrowserCredential first; MfaBrowserCredential second;
        await using (var db = new NoCtfDbContext(options))
        {
            await db.Database.MigrateAsync(ct); db.Users.Add(User(userId, UserKind.Human)); await db.SaveChangesAsync(ct);
            var store = new MfaAuthenticationStore(db, crypto, protector, clock);
            var actor = new MfaActor(userId, 0, new(AuthenticationMethod.Password, clock.GetUtcNow()));
            var started = await store.BeginOwnEnrollmentAsync(actor, ct);
            var begun = await store.BeginEnrollmentAsync(started.Value!.Browser, ct);
            var code = Code(begun.Value!.Secret!, clock);
            await Assert.That((await store.ConfirmEnrollmentAsync(started.Value.Browser, code, ct)).FailureCode).IsEqualTo(MfaFailure.NotApplicable);
            var confirmed = await store.ConfirmEnrollmentAsync(started.Value.Browser, code, ct, accountManagement: true);
            await Assert.That(confirmed.Succeeded).IsTrue(); recoveryCodes = confirmed.Value!.RecoveryCodes!;
            await Assert.That(await store.ValidateContextAsync(userId, 0, actor.Authentication, ct)).IsEqualTo(MfaFailure.AccountUnavailable);
            var account = (await store.ReadAccountAsync(userId, ct))!;
            (_, first) = await store.CreateFlowAsync(new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Login, ct);
            (_, second) = await store.CreateFlowAsync(new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Login, ct);
        }
        var attempts = await Task.WhenAll(new[] { first, second }.Select(async browser =>
        {
            await using var db = new NoCtfDbContext(options);
            return await new MfaAuthenticationStore(db, crypto, protector, clock).VerifyAsync(browser, new(MfaVerificationMethod.RecoveryCode, recoveryCodes[0]), ct);
        }));
        await Assert.That(attempts.Count(value => value.Succeeded)).IsEqualTo(1);
        await using (var db = new NoCtfDbContext(options))
        {
            await Assert.That(await db.UserMfaRecoveryCodes.CountAsync(value => value.ConsumedAt != null, ct)).IsEqualTo(1);
            var store = new MfaAuthenticationStore(db, crypto, protector, clock); var account = (await store.ReadAccountAsync(userId, ct))!;
            var (_, pending) = await store.CreateFlowAsync(new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Login, ct);
            db.ChangeTracker.Clear(); var settings = await db.PlatformSettings.SingleAsync(ct); settings.MfaPolicyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct);
            await Assert.That((await store.VerifyAsync(pending, new(MfaVerificationMethod.RecoveryCode, recoveryCodes[1]), ct)).FailureCode).IsEqualTo(MfaFailure.PolicyChanged);
            await Assert.That(await db.UserMfaRecoveryCodes.CountAsync(value => value.ConsumedAt != null, ct)).IsEqualTo(1);
            clock.Advance(TimeSpan.FromSeconds(30));
            var credential = await db.UserTotpCredentials.SingleAsync(ct);
            var secret = protector.Unprotect(credential.SecretCiphertext, PlatformSecretPurpose.TotpCredentialSecret, userId, credential.Id);
            var actor = new MfaActor(userId, 1, new(AuthenticationMethod.Password, clock.GetUtcNow(), MfaSource.RecoveryCode, clock.GetUtcNow(), credential.Id));
            var proof = await store.BeginStepUpAsync(actor, MfaOperation.RegenerateRecoveryCodes, userId, ct);
            await Assert.That((await store.VerifyStepUpAsync(proof.Value!.Browser, new(MfaVerificationMethod.Totp, Code(secret, clock)), ct)).Succeeded).IsTrue();
            var regenerated = await store.RegenerateRecoveryCodesAsync(actor, proof.Value.Browser, ct);
            await Assert.That(regenerated.Value!.Count).IsEqualTo(10);
            var renewed = (await store.ReadAccountAsync(userId, ct))!;
            await Assert.That(renewed.User.TokenVersion).IsEqualTo(2);
            var (_, fresh) = await store.CreateFlowAsync(new(renewed.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Login, ct);
            await Assert.That((await store.VerifyAsync(fresh, new(MfaVerificationMethod.RecoveryCode, recoveryCodes[2]), ct)).FailureCode).IsEqualTo(MfaFailure.InvalidCode);
            await Assert.That((await store.VerifyAsync(fresh, new(MfaVerificationMethod.RecoveryCode, regenerated.Value[0]), ct)).Succeeded).IsTrue();
        }
    });

    [Test, Timeout(300_000)]
    public Task Oidc_rules_are_relational_and_provider_changes_invalidate_pending_primary_proof(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = Container(); await postgres.StartAsync(ct); var options = Options(postgres);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z")); var crypto = new MfaCryptography(); var protector = Protector();
        await using var db = new NoCtfDbContext(options); await db.Database.MigrateAsync(ct);
        var userId = Guid.NewGuid(); db.Users.Add(User(userId, UserKind.Human));
        var providerId = Guid.NewGuid(); var policyId = Guid.NewGuid();
        db.Set<NoCTF.Domain.Platform.OidcSsoProviderConfiguration>().Add(new() { Id = providerId, Name = "Verified OIDC", Issuer = "https://identity.test", DiscoveryUrl = "https://identity.test/.well-known/openid-configuration", ClientId = "test", Enabled = true, AllowLogin = true, MfaTrustEnabled = true, MfaTrustPolicyId = policyId,
            MfaAcrEntries = [new() { SsoProviderId = providerId, Position = 0, Value = "urn:test:mfa" }] });
        var settings = await db.PlatformSettings.SingleAsync(ct); settings.SsoEnabled = true; settings.SsoPublicBaseUrl = "https://noctf.test";
        await db.SaveChangesAsync(ct); var store = new MfaAuthenticationStore(db, crypto, protector, clock);
        var account = (await store.ReadAccountAsync(userId, ct))!;
        var config = await store.ReadConfigurationAsync(new(userId, 0, new(AuthenticationMethod.Password, clock.GetUtcNow())), ct);
        await Assert.That(config.Value!.Providers.Count).IsEqualTo(1);
        await Assert.That(config.Value.Providers[0].Trust.AcrValues.Single()).IsEqualTo("urn:test:mfa");
        var proof = new OidcMfaProof(providerId, policyId, clock.GetUtcNow());
        await Assert.That(await store.IsOidcProofCurrentAsync(proof, ct)).IsTrue();
        var (_, browser) = await store.CreateFlowAsync(new(account.User, AuthenticationMethod.Oidc, clock.GetUtcNow(), ProviderId: providerId), MfaChallengePurpose.Enrollment, ct);
        db.ChangeTracker.Clear(); var provider = await db.Set<NoCTF.Domain.Platform.OidcSsoProviderConfiguration>().SingleAsync(ct); provider.ClientId = "changed"; provider.MfaTrustPolicyId = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await Assert.That(await store.IsOidcProofCurrentAsync(proof, ct)).IsFalse();
        await Assert.That((await store.BeginEnrollmentAsync(browser, ct)).FailureCode).IsEqualTo(MfaFailure.PolicyChanged);
    });

    [Test, Timeout(300_000)]
    public Task Mail_dispatch_is_single_winner_and_abandoned_dispatch_is_redispatchable(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = Container(); await postgres.StartAsync(ct); var options = Options(postgres);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z")); var userId = Guid.NewGuid(); var challengeId = Guid.NewGuid();
        await using (var db = new NoCtfDbContext(options))
        {
            await db.Database.MigrateAsync(ct); db.Users.Add(User(userId, UserKind.Human));
            db.MfaChallenges.Add(new() { Id = challengeId, UserId = userId, CreatedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddMinutes(10), Purpose = MfaChallengePurpose.Enrollment, State = MfaChallengeState.Completed, MailState = MfaMailState.Pending });
            await db.SaveChangesAsync(ct);
        }
        var sends = 0; var delivery = NSubstitute.Substitute.For<IMfaEmailDelivery>();
        delivery.SendSecurityNotificationAsync(userId, MfaOperation.EnableTotp, NSubstitute.Arg.Any<CancellationToken>()).Returns(async _ => { Interlocked.Increment(ref sends); await Task.Delay(50, ct); return MfaMailDeliveryState.Sent; });
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = new NoCtfDbContext(options);
            await new NoCTF.Worker.Authentication.MfaMailHandler(db, Protector(), delivery, clock).Handle(new NoCTF.Application.Messaging.SendMfaMail(challengeId), ct);
        }));
        await Assert.That(sends).IsEqualTo(1);
        await using (var db = new NoCtfDbContext(options))
        {
            var challenge = await db.MfaChallenges.SingleAsync(ct); await Assert.That(challenge.MailState).IsEqualTo(MfaMailState.Sent);
            challenge.MailState = MfaMailState.Sending; challenge.MailAttemptedAt = clock.GetUtcNow().AddMinutes(-6); await db.SaveChangesAsync(ct);
        }
        await using (var db = new NoCtfDbContext(options))
            await new NoCTF.Worker.Authentication.MfaMailHandler(db, Protector(), delivery, clock).Handle(new NoCTF.Application.Messaging.SendMfaMail(challengeId), ct);
        await Assert.That(sends).IsEqualTo(2);
    });

    [Test, Timeout(300_000)]
    public Task Rebind_keeps_the_old_credential_until_confirmation_and_rolls_back_a_failed_replacement(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = Container(); await postgres.StartAsync(ct); var options = Options(postgres);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z")); var crypto = new MfaCryptography(); var protector = Protector();
        var userId = Guid.NewGuid(); var oldId = Guid.NewGuid(); var oldSecret = crypto.GenerateSecret(); MfaBrowserCredential browser; string secret;
        await using (var db = new NoCtfDbContext(options))
        {
            await db.Database.MigrateAsync(ct); db.Users.Add(User(userId, UserKind.Human));
            db.UserTotpCredentials.Add(new() { Id = oldId, UserId = userId, SecretCiphertext = protector.Protect(oldSecret, PlatformSecretPurpose.TotpCredentialSecret, userId, oldId), EnabledAt = clock.GetUtcNow(), RecoveryBatchId = Guid.NewGuid() });
            await db.SaveChangesAsync(ct); var store = new MfaAuthenticationStore(db, crypto, protector, clock);
            var actor = new MfaActor(userId, 0, new(AuthenticationMethod.Password, clock.GetUtcNow(), MfaSource.Totp, clock.GetUtcNow(), oldId));
            var proof = await store.BeginStepUpAsync(actor, MfaOperation.RebindTotp, userId, ct);
            await Assert.That((await store.VerifyStepUpAsync(proof.Value!.Browser, new(MfaVerificationMethod.Totp, Code(oldSecret, clock)), ct)).Succeeded).IsTrue();
            var begun = await store.BeginRebindAsync(actor, proof.Value.Browser, ct); browser = begun.Value!.Browser;
            var enrolled = await store.BeginEnrollmentAsync(browser, ct); secret = enrolled.Value!.Secret!;
            await Assert.That((await db.UserTotpCredentials.SingleAsync(ct)).Id).IsEqualTo(oldId);
        }
        var failingOptions = new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(new FailCredentialInsertion()).Options;
        await using (var db = new NoCtfDbContext(failingOptions))
        {
            var result = await new MfaAuthenticationStore(db, crypto, protector, clock).ConfirmEnrollmentAsync(browser, Code(secret, clock), ct, accountManagement: true);
            await Assert.That(result.FailureCode).IsEqualTo(MfaFailure.DependencyUnavailable);
        }
        await using (var db = new NoCtfDbContext(options))
        {
            await Assert.That((await db.UserTotpCredentials.SingleAsync(ct)).Id).IsEqualTo(oldId);
            await Assert.That((await db.Users.SingleAsync(ct)).TokenVersion).IsEqualTo(0);
            await Assert.That((await db.MfaChallenges.SingleAsync(value => value.Id == browser.ChallengeId, ct)).State).IsEqualTo(MfaChallengeState.Pending);
            var result = await new MfaAuthenticationStore(db, crypto, protector, clock).ConfirmEnrollmentAsync(browser, Code(secret, clock), ct, accountManagement: true);
            await Assert.That(result.Succeeded).IsTrue(); await Assert.That(result.Value!.User.TokenVersion).IsEqualTo(1);
            await Assert.That((await db.UserTotpCredentials.SingleAsync(ct)).Id == oldId).IsFalse();
        }
    });

    [Test, Timeout(300_000)]
    public Task Step_up_is_bound_to_operation_and_required_factors_cannot_be_disabled(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = Container(); await postgres.StartAsync(ct); var options = Options(postgres);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z")); var crypto = new MfaCryptography(); var protector = Protector();
        var userId = Guid.NewGuid(); var credentialId = Guid.NewGuid(); var secret = crypto.GenerateSecret();
        await using var db = new NoCtfDbContext(options); await db.Database.MigrateAsync(ct);
        var user = User(userId, UserKind.Human); user.MfaRequired = true; db.Users.Add(user);
        db.UserTotpCredentials.Add(new() { Id = credentialId, UserId = userId, SecretCiphertext = protector.Protect(secret, PlatformSecretPurpose.TotpCredentialSecret, userId, credentialId), EnabledAt = clock.GetUtcNow(), RecoveryBatchId = Guid.NewGuid() });
        await db.SaveChangesAsync(ct);
        var store = new MfaAuthenticationStore(db, crypto, protector, clock);
        var actor = new MfaActor(userId, 0, new(AuthenticationMethod.Password, clock.GetUtcNow(), MfaSource.Totp, clock.GetUtcNow(), credentialId));
        var step = await store.BeginStepUpAsync(actor, MfaOperation.DisableTotp, userId, ct);
        await Assert.That((await store.VerifyStepUpAsync(step.Value!.Browser, new(MfaVerificationMethod.Totp, Code(secret, clock)), ct)).Succeeded).IsTrue();
        await Assert.That((await store.RegenerateRecoveryCodesAsync(actor, step.Value.Browser, ct)).FailureCode).IsEqualTo(MfaFailure.StepUpRequired);
        await Assert.That((await store.DisableAsync(actor, step.Value.Browser, ct)).FailureCode).IsEqualTo(MfaFailure.PolicyRequiresMfa);
        await Assert.That(await db.UserTotpCredentials.CountAsync(ct)).IsEqualTo(1);
        db.ChangeTracker.Clear(); var current = await db.Users.SingleAsync(ct); current.MfaRequired = false; await db.SaveChangesAsync(ct);
        await Assert.That((await store.DisableAsync(actor, step.Value.Browser, ct)).Succeeded).IsTrue();
        await Assert.That(await db.UserTotpCredentials.CountAsync(ct)).IsEqualTo(0);
        await Assert.That((await store.DisableAsync(actor, step.Value.Browser, ct)).Succeeded).IsFalse();
    });

    [Test, Timeout(300_000)]
    public Task Recovery_grants_are_single_use_and_cannot_bind_before_primary_authentication(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = Container(); await postgres.StartAsync(ct); var options = Options(postgres);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z")); var crypto = new MfaCryptography(); var protector = Protector();
        var userId = Guid.NewGuid();
        await using var db = new NoCtfDbContext(options); await db.Database.MigrateAsync(ct);
        var user = User(userId, UserKind.Human); user.EmailVerifiedAt = clock.GetUtcNow(); db.Users.Add(user);
        var settings = await db.PlatformSettings.SingleAsync(ct); settings.EmailSmtpHost = "smtp.test.invalid"; settings.EmailSmtpFromAddress = "no-reply@test.invalid";
        settings.EmailSmtpSecurityMode = SmtpSecurityMode.None; settings.EmailPublicBaseUrl = "https://noctf.test";
        await db.SaveChangesAsync(ct); var store = new MfaAuthenticationStore(db, crypto, protector, clock);
        var granted = await store.GrantRecoveryAsync(null, null, userId, "Offline operator verified account ownership", ct);
        await Assert.That(granted.Succeeded).IsTrue();
        var grant = await db.MfaChallenges.SingleAsync(value => value.Id == granted.Value!.Id, ct);
        var token = protector.Unprotect(grant.RecoveryGrantCiphertext!, PlatformSecretPurpose.MfaRecoveryGrant, userId, grant.Id);
        var exchange = await store.ExchangeRecoveryAsync(token, ct);
        await Assert.That(exchange.Succeeded).IsTrue();
        await Assert.That((await store.ExchangeRecoveryAsync(token, ct)).Succeeded).IsFalse();
        await Assert.That((await store.BeginEnrollmentAsync(exchange.Value!.Browser, ct)).FailureCode).IsEqualTo(MfaFailure.PrimaryAuthenticationRequired);
        var account = (await store.ReadAccountAsync(userId, ct))!;
        await Assert.That((await store.ApplyRecoveryPrimaryAsync(exchange.Value.Browser, new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), ct)).Succeeded).IsTrue();
        var enrollment = await store.BeginEnrollmentAsync(exchange.Value.Browser, ct);
        var confirmed = await store.ConfirmEnrollmentAsync(exchange.Value.Browser, Code(enrollment.Value!.Secret!, clock), ct);
        await Assert.That(confirmed.Succeeded).IsTrue();
        await Assert.That(confirmed.Value!.User.TokenVersion).IsEqualTo(2);
        await Assert.That(confirmed.Value.Context.MfaSource).IsEqualTo(MfaSource.Totp);
    });

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
        foreach (var policy in Enum.GetValues<MfaPolicy>())
        {
            db.ChangeTracker.Clear(); var currentPolicy = await db.PlatformSettings.SingleAsync(ct);
            currentPolicy.MfaPolicy = policy; currentPolicy.MfaPolicyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct);
            await Assert.That(await store.ValidateContextAsync(botId, 0, null, ct)).IsNull();
            var batch = await store.ValidateContextsAsync([new("bot", botId, 0, null)], ct);
            await Assert.That(batch["bot"]).IsNull();
        }
        await Assert.That(await store.ValidateContextAsync(userId, 0, null, ct)).IsEqualTo(MfaFailure.PrimaryAuthenticationRequired);
        await Assert.That((await store.ReadFlowAsync(blocked, ct)).FailureCode).IsEqualTo(MfaFailure.PolicyChanged);
    });

    private sealed class FailCredentialInsertion : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<NoCTF.Domain.Identity.Mfa.UserTotpCredential>().Any(value => value.State == EntityState.Added))
                throw new InvalidOperationException("Simulated persistence failure after old credential removal.");
            return ValueTask.FromResult(result);
        }
    }

    private static PostgreSqlContainer Container() => new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
        .WithDatabase("mfa").WithUsername("postgres").WithPassword("postgres").Build();
    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) => new DbContextOptionsBuilder<NoCtfDbContext>()
        .UseNpgsql(postgres.GetConnectionString(), value => value.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
    private static PlatformSecretProtector Protector() => new(Microsoft.Extensions.Options.Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
    private static User User(Guid id, UserKind kind) => new() { Id = id, UserName = id.ToString("N"), NormalizedUserName = id.ToString("N").ToUpperInvariant(),
        Email = $"{id:N}@test.invalid", PasswordHash = "test", Kind = kind, Role = UserRole.Administrator, AccountStatus = UserAccountStatus.Active };
    private static string Code(string secret, FakeTimeProvider clock) => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(clock.GetUtcNow().UtcDateTime);
}
