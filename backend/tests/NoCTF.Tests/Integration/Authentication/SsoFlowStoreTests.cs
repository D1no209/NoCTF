using NSubstitute;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class SsoFlowStoreTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Callback_claim_and_completion_are_single_consumer_and_browser_bound(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_sso")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(
                    typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention().Options;
            await using (var db = new NoCtfDbContext(options))
                await db.Database.MigrateAsync(cancellationToken);
            var store = new PersistedSsoFlowStore(
                new TestDbContextFactory(options), TimeProvider.System);
            var now = DateTimeOffset.UtcNow;
            var providerId = Guid.CreateVersion7(now);
            var flow = new SsoFlowRecord(
                Guid.CreateVersion7(now),
                providerId,
                SsoProtocol.Oidc,
                SsoFlowIntent.Login,
                "BROWSER-HASH",
                SsoFlowState.Pending,
                "correlation-token",
                "provider-fingerprint",
                "/competitions",
                now,
                now.Add(SsoRules.FlowLifetime),
                Nonce: "nonce",
                PkceVerifier: "verifier");
            await Assert.That(await store.CreateAsync(flow, cancellationToken)).IsTrue();

            var wrongBrowser = await store.ClaimCallbackAsync(
                flow.CorrelationToken,
                providerId,
                "OTHER-BROWSER",
                cancellationToken);
            await Assert.That(wrongBrowser.State).IsEqualTo(SsoFlowClaimState.InvalidCorrelation);

            var claims = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
                store.ClaimCallbackAsync(
                    flow.CorrelationToken,
                    providerId,
                    flow.BrowserIdHash,
                    cancellationToken)));
            await Assert.That(claims.Count(result => result.State == SsoFlowClaimState.Claimed))
                .IsEqualTo(1);
            var claimed = claims.Single(result => result.State == SsoFlowClaimState.Claimed);
            await Assert.That(await store.SaveAuthenticatedAsync(
                flow.Id,
                "wrong-processing-token",
                Identity(providerId),
                cancellationToken)).IsFalse();
            await Assert.That(await store.SaveAuthenticatedAsync(
                flow.Id,
                claimed.ProcessingToken!,
                Identity(providerId),
                cancellationToken)).IsTrue();

            var readable = await store.ReadAsync(flow.Id, flow.BrowserIdHash, cancellationToken);
            await Assert.That(readable.State).IsEqualTo(SsoFlowReadState.Available);
            await Assert.That(readable.Flow!.State).IsEqualTo(SsoFlowState.Authenticated);
            await Assert.That(readable.Flow.ExternalIdentity!.Subject).IsEqualTo("subject-1");

            var accounts = Substitute.For<ISsoAccountStore>();
            accounts.FindByExternalIdentityAsync(
                    Arg.Any<SsoExternalIdentity>(),
                    Arg.Any<CancellationToken>())
                .Returns(new SsoExternalAccountLookup(SsoExternalAccountLookupState.NotLinked));
            var providers = Substitute.For<ISsoProviderRuntimeReader>();
            providers.FindAsync(providerId, Arg.Any<CancellationToken>())
                .Returns(Provider(providerId));
            var activities = Substitute.For<IAccountActivityRecorder>();
            var complete = new CompleteSsoLogin(
                store,
                accounts,
                providers,
                new NoCTF.Application.Authentication.Mfa.CompleteAuthentication(MfaTestSupport.Unrequired(), Substitute.For<IAccessTokenIssuer>(), TimeProvider.System),
                activities,
                TimeProvider.System);

            var unlinked = await complete.ExecuteAsync(
                flow.Id, flow.BrowserIdHash, cancellationToken);
            await Assert.That(unlinked.FailureCode).IsEqualTo(SsoFailureCode.IdentityNotLinked);
            await Assert.That((await store.ReadAsync(
                flow.Id, flow.BrowserIdHash, cancellationToken)).State)
                .IsEqualTo(SsoFlowReadState.NotFound);
            var replay = await complete.ExecuteAsync(
                flow.Id, flow.BrowserIdHash, cancellationToken);
            await Assert.That(replay.FailureCode).IsEqualTo(SsoFailureCode.FlowExpired);
            await activities.Received(1).RecordSsoAsync(
                null,
                providerId,
                false,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>());
        });
    }

    private static SsoExternalIdentity Identity(Guid providerId) => new(
        providerId,
        SsoProtocol.Oidc,
        "https://issuer.example.test",
        "subject-1",
        "Test User");

    private static SsoProviderRuntimeConfiguration Provider(Guid providerId) => new(
        true,
        "https://ctf.example.test",
        providerId,
        "Example OIDC",
        SsoProtocol.Oidc,
        true,
        true,
        true,
        10,
        ["id.example.test"],
        "provider-fingerprint",
        new OidcSsoRuntimeConfiguration(
            "https://issuer.example.test",
            "https://id.example.test/.well-known/openid-configuration",
            "noctf",
            "secret",
            ["openid"],
            false,
            "name"),
        null);

}
