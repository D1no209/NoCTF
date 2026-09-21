using NSubstitute;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using StackExchange.Redis;
using Testcontainers.Redis;

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
            await using var target = await StartRedisAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(target.ConnectionString);
            var store = new RedisSsoFlowStore(redis);
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
                Substitute.For<IAccessTokenIssuer>(),
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

    private static async Task<RedisLease> StartRedisAsync(CancellationToken ct)
    {
        var external = Environment.GetEnvironmentVariable("NOCTF_TEST_REDIS");
        if (!string.IsNullOrWhiteSpace(external))
            return new(external, null);
        var container = new RedisBuilder(
            "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
        await container.StartAsync(ct);
        return new(container.GetConnectionString(), container);
    }

    private sealed class RedisLease(string connectionString, RedisContainer? container)
        : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;
        public ValueTask DisposeAsync() => container?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
