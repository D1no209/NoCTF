using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Authentication.Passkeys;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Authentication.Mfa;
using NoCTF.Infrastructure.Authentication.Passkeys;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration"), NotInParallel]
public sealed class PasskeyPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Passkey_primary_authentication_keeps_Totp_required_and_sensitive_proofs_are_operation_bound(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var pg = Container(); await pg.StartAsync(ct); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var services = Services(pg, clock); using var scope = services.CreateScope(); var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<NoCtfDbContext>(); await db.Database.MigrateAsync(ct); var id = Guid.NewGuid(); db.Users.Add(User(id)); await db.SaveChangesAsync(ct);
        using var authenticator = new SoftwarePasskeyAuthenticator(); var store = provider.GetRequiredService<IPasskeyStore>();
        var actor = new MfaActor(id, 0, new(AuthenticationMethod.Password, clock.GetUtcNow()));
        var begun = (await store.BeginRegistrationAsync(actor, null, "Key", "https://noctf.test", ct)).Value!;
        var registered = (await store.FinishRegistrationAsync(actor, begun.Browser, authenticator.Register(begun.OptionsJson, "https://noctf.test"), "https://noctf.test", ct)).Value!;
        var mfa = provider.GetRequiredService<IMfaAuthenticationStore>(); var management = provider.GetRequiredService<IMfaManagementStore>();
        var account = (await mfa.ReadAccountAsync(id, ct))!;
        var (_, enrollment) = await mfa.CreateFlowAsync(new(account.User, AuthenticationMethod.Password, clock.GetUtcNow()), MfaChallengePurpose.Enrollment, ct);
        var setup = (await mfa.BeginEnrollmentAsync(enrollment, ct)).Value!;
        string Code() => new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(setup.Secret!)).ComputeTotp(clock.GetUtcNow().UtcDateTime);
        var bound = (await mfa.ConfirmEnrollmentAsync(enrollment, Code(), ct)).Value!; clock.Advance(TimeSpan.FromSeconds(30));
        var issuer = NSubstitute.Substitute.For<NoCTF.Application.Authentication.Account.IAccessTokenIssuer>();
        var authenticate = new AuthenticateWithPasskey(store, new CompleteAuthentication(mfa, issuer, clock), clock);
        var login = (await store.BeginLoginAsync("https://noctf.test", "/", ct)).Value!;
        var result = await authenticate.ExecuteAsync(login.Browser, authenticator.Assert(login.OptionsJson, "https://noctf.test"), "https://noctf.test", null, ct);
        await Assert.That(result.Value!.State).IsEqualTo(AuthenticationState.MfaRequired);
        await Assert.That(result.Value.Access).IsNull(); await Assert.That(result.Value.Refresh).IsNull();
        var completed = (await mfa.VerifyAsync(result.Value.Browser!, new(MfaVerificationMethod.Totp, Code()), ct)).Value!;
        await Assert.That(completed.Context.Method).IsEqualTo(AuthenticationMethod.Passkey);
        await Assert.That(completed.Context.PrimaryCredentialId).IsEqualTo(registered.Id);
        await Assert.That(completed.Context.HasMfa).IsTrue(); await Assert.That(completed.Context.IsWellFormed).IsTrue();
        var current = new MfaActor(id, completed.User.TokenVersion, completed.Context);
        await Assert.That((await store.RemoveAsync(current, null, registered.Id, ct)).FailureCode).IsEqualTo(PasskeyFailure.StepUpRequired);
        var proof = (await management.BeginStepUpAsync(current, MfaOperation.RemovePasskey, registered.Id, ct)).Value!;
        await Assert.That((await management.VerifyStepUpAsync(proof.Browser, new(MfaVerificationMethod.RecoveryCode, bound.RecoveryCodes![0]), ct)).Succeeded).IsTrue();
        await Assert.That((await store.RenameAsync(current, proof.Browser, registered.Id, "Wrong operation", ct)).FailureCode).IsEqualTo(PasskeyFailure.StepUpRequired);
        var (_, pending) = await mfa.CreateFlowAsync(new(completed.User, AuthenticationMethod.Passkey, clock.GetUtcNow(), CredentialId: registered.Id), MfaChallengePurpose.Login, ct);
        await Assert.That((await store.RemoveAsync(current, proof.Browser, registered.Id, ct)).Succeeded).IsTrue();
        await Assert.That((await mfa.VerifyAsync(pending, new(MfaVerificationMethod.RecoveryCode, bound.RecoveryCodes[1]), ct)).FailureCode).IsEqualTo(MfaFailure.AccountUnavailable);
    });

    [Test, Timeout(300_000)]
    public Task Registration_login_single_use_and_revocation_are_relational(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var pg = Container(); await pg.StartAsync(ct);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var id = Guid.NewGuid();
        using var services = Services(pg, clock);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>(); await db.Database.MigrateAsync(ct);
            db.Users.Add(User(id)); await db.SaveChangesAsync(ct);
        }
        using var authenticator = new SoftwarePasskeyAuthenticator(); var actor = new MfaActor(id, 0, new(AuthenticationMethod.Password, clock.GetUtcNow()));
        Guid credentialId;
        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPasskeyStore>();
            var start = await store.BeginRegistrationAsync(actor, null, "Security key", "https://noctf.test", ct);
            await Assert.That(start.Succeeded).IsTrue();
            var payload = authenticator.Register(start.Value!.OptionsJson, "https://noctf.test");
            var registered = await store.FinishRegistrationAsync(actor, start.Value.Browser, payload, "https://noctf.test", ct);
            await Assert.That(registered.Succeeded).IsTrue(); credentialId = registered.Value!.Id;
            await Assert.That((await store.FinishRegistrationAsync(actor, start.Value.Browser, payload, "https://noctf.test", ct)).Succeeded).IsFalse();
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
            await Assert.That((await db.Users.SingleAsync(ct)).TokenVersion).IsEqualTo(1);
            await Assert.That(await db.UserPasskeys.CountAsync(ct)).IsEqualTo(1);
        }
        PasskeyStartedCeremony login;
        using (var scope = services.CreateScope()) login = (await scope.ServiceProvider.GetRequiredService<IPasskeyStore>().BeginLoginAsync("https://noctf.test", "/competitions", ct)).Value!;
        var assertion = authenticator.Assert(login.OptionsJson, "https://noctf.test");
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var scope = services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IPasskeyStore>().FinishLoginAsync(login.Browser, assertion, "https://noctf.test", ct);
        }));
        await Assert.That(results.Count(value => value.Succeeded)).IsEqualTo(1);
        using (var scope = services.CreateScope())
        {
            var provider = scope.ServiceProvider; var mfa = provider.GetRequiredService<IMfaAuthenticationStore>();
            var context = new AuthenticationContext(AuthenticationMethod.Passkey, clock.GetUtcNow(), PrimaryCredentialId: credentialId);
            await Assert.That(await mfa.ValidateContextAsync(id, 1, context, ct)).IsNull();
            var store = provider.GetRequiredService<IPasskeyStore>();
            var current = new MfaActor(id, 1, context);
            var renamed = await store.RenameAsync(current, null, credentialId, "Laptop", ct); await Assert.That(renamed.Value!.Name).IsEqualTo("Laptop");
            await Assert.That((await store.RemoveAsync(current, null, credentialId, ct)).Succeeded).IsTrue();
            await Assert.That(await mfa.ValidateContextAsync(id, 1, context, ct)).IsEqualTo(MfaFailure.AccountUnavailable);
            await Assert.That(await provider.GetRequiredService<NoCtfDbContext>().UserPasskeys.CountAsync(ct)).IsEqualTo(0);
        }
    });

    [Test, Timeout(300_000)]
    public Task Browser_account_policy_and_expiry_bindings_reject_misuse(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var pg = Container(); await pg.StartAsync(ct); var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var services = Services(pg, clock); var id = Guid.NewGuid(); var other = Guid.NewGuid(); var bot = Guid.NewGuid();
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>(); await db.Database.MigrateAsync(ct);
        db.Users.AddRange(User(id), User(other), User(bot, UserKind.Bot)); await db.SaveChangesAsync(ct);
        var store = scope.ServiceProvider.GetRequiredService<IPasskeyStore>();
        var actor = new MfaActor(id, 0, new(AuthenticationMethod.Password, clock.GetUtcNow())); using var auth = new SoftwarePasskeyAuthenticator();
        var begun = await store.BeginRegistrationAsync(actor, null, "Phone", "https://noctf.test", ct); var value = begun.Value!;
        var payload = auth.Register(value.OptionsJson, "https://noctf.test");
        await Assert.That((await store.FinishRegistrationAsync(actor, value.Browser with { Secret = new string('x', 43) }, payload, "https://noctf.test", ct)).FailureCode).IsEqualTo(PasskeyFailure.InvalidBrowser);
        await Assert.That((await store.FinishRegistrationAsync(new(other, 0, actor.Authentication), value.Browser, payload, "https://noctf.test", ct)).FailureCode).IsEqualTo(PasskeyFailure.AccountUnavailable);
        await Assert.That((await store.BeginRegistrationAsync(new(bot, 0, actor.Authentication), null, "Bot", "https://noctf.test", ct)).Succeeded).IsFalse();
        db.ChangeTracker.Clear(); var settings = await db.PlatformSettings.SingleAsync(ct); settings.MfaPolicyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await Assert.That((await store.FinishRegistrationAsync(actor, value.Browser, payload, "https://noctf.test", ct)).FailureCode).IsEqualTo(PasskeyFailure.PolicyChanged);
        var expiry = (await store.BeginLoginAsync("https://noctf.test", "/", ct)).Value!; clock.Advance(TimeSpan.FromMinutes(4));
        await Assert.That((await store.FinishLoginAsync(expiry.Browser, "{}", "https://noctf.test", ct)).FailureCode).IsEqualTo(PasskeyFailure.FlowExpired);
        await Assert.That(await db.UserPasskeys.CountAsync(ct)).IsEqualTo(0);
    });

    private static PostgreSqlContainer Container() => new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").WithDatabase("passkeys").WithUsername("postgres").WithPassword("postgres").Build();
    private static User User(Guid id, UserKind kind = UserKind.Human) => new() { Id = id, UserName = id.ToString("N"), NormalizedUserName = id.ToString("N").ToUpperInvariant(), Email = id + "@test.invalid", NormalizedEmail = (id + "@test.invalid").ToUpperInvariant(), PasswordHash = "test", Kind = kind };
    private static ServiceProvider Services(PostgreSqlContainer pg, FakeTimeProvider clock)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(pg.GetConnectionString(), value => value.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention());
        services.AddIdentityCore<User>().AddUserStore<PasskeyIdentityStore>();
        services.Configure<IdentityPasskeyOptions>(options => { options.ServerDomain = "noctf.test"; options.UserVerificationRequirement = "required"; options.ValidateOrigin = value => ValueTask.FromResult(value.Origin == "https://noctf.test" && !value.CrossOrigin); });
        services.AddScoped<IPasskeyHandler<User>, PasskeyHandler<User>>(); services.AddScoped<IPasskeyProtocol, PasskeyProtocol>();
        services.AddSingleton(Options.Create(new PasskeyOptions { ServerDomain = "noctf.test", AllowedOrigins = ["https://noctf.test"] }));
        services.AddSingleton(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
        services.AddSingleton<PlatformSecretProtector>(); services.AddSingleton<IMfaCryptography, MfaCryptography>();
        services.AddScoped<IMfaAuthenticationStore, MfaAuthenticationStore>(); services.AddScoped<IMfaSensitiveOperationProof, MfaAuthenticationStore>();
        services.AddScoped<IMfaManagementStore, MfaAuthenticationStore>();
        services.AddScoped<IPasskeyStore, PasskeyStore>(); return services.BuildServiceProvider();
    }
}
