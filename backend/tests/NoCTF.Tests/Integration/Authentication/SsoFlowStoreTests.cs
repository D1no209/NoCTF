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

            var consumed = await store.ConsumeAuthenticatedAsync(
                flow.Id, flow.BrowserIdHash, cancellationToken);
            await Assert.That(consumed.State).IsEqualTo(SsoFlowReadState.Available);
            await Assert.That(consumed.Flow!.State).IsEqualTo(SsoFlowState.Consumed);
            await Assert.That((await store.ReadAsync(
                flow.Id, flow.BrowserIdHash, cancellationToken)).State)
                .IsEqualTo(SsoFlowReadState.NotFound);
        });
    }

    private static SsoExternalIdentity Identity(Guid providerId) => new(
        providerId,
        SsoProtocol.Oidc,
        "https://issuer.example.test",
        "subject-1",
        "Test User");

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
