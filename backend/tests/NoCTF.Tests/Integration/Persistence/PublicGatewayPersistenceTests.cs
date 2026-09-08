using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.PublicAccess;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class PublicGatewayPersistenceTests
{
    [Test, Timeout(120_000)]
    public async Task Stop_waits_for_local_lease_activation_and_a_late_activation_is_rejected(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            var policy = new PublicGatewayPolicy(true, "gateway", "https://public.example.test", ["https://direct.example.test"], "203.0.113.1", null, 8);
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(ct); await fixture.SeedAsync(setup, ct);
                var runtime = await setup.RuntimeInstances.SingleAsync(x => x.Id == fixture.RuntimeIds[0], ct);
                runtime.State = NoCTF.Domain.Runtime.RuntimeState.Running; runtime.RunnerId = "runner"; runtime.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1);
                var settings = await setup.PlatformSettings.SingleAsync(ct);
                settings.PublicGatewayEnabled = true; settings.PublicGatewayConnectorId = policy.ConnectorId;
                settings.PublicGatewayOrigin = policy.PublicOrigin; settings.PublicGatewayDirectOrigins = policy.DirectOrigins.ToArray();
                settings.PublicGatewayRuntimeHost = policy.PublicRuntimeHost; settings.PublicGatewayMaxPorts = policy.MaxPublishedPorts;
                await setup.SaveChangesAsync(ct);
            }
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var leaseDb = new NoCtfDbContext(options);
            await using var stopDb = new NoCtfDbContext(options);
            var lease = new PublicGatewayLeaseGuard(leaseDb, TimeProvider.System).RenewAsync(fixture.RuntimeIds[0], "runner", policy,
                async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); }, ct);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            var stop = stopDb.RuntimeInstances.Where(x => x.Id == fixture.RuntimeIds[0])
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.State, NoCTF.Domain.Runtime.RuntimeState.Stopped), ct);
            await Task.Delay(100, ct);
            await Assert.That(stop.IsCompleted).IsFalse();
            release.SetResult();
            await Task.WhenAll(lease, stop);
            var activated = false;
            await Assert.That(async () => await new PublicGatewayLeaseGuard(leaseDb, TimeProvider.System).RenewAsync(fixture.RuntimeIds[0], "runner", policy,
                (_, _) => { activated = true; return Task.CompletedTask; }, ct)).Throws<InvalidOperationException>();
            await Assert.That(activated).IsFalse();
        });
    }

    [Test, Timeout(120_000)]
    public async Task Generated_upgrade_preserves_existing_branding_and_gateway_settings_survive_context_restart(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            using var cacheServices = new ServiceCollection().AddFusionCache(NoCtfCacheNames.ReadModels).Services.BuildServiceProvider();
            var caches = cacheServices.GetRequiredService<IFusionCacheProvider>();
            await using (var previous = new NoCtfDbContext(options))
            {
                await previous.GetService<IMigrator>().MigrateAsync("20260907190934_PrivateSchoolIdentityAndSourceAddresses", ct);
                await previous.Database.ExecuteSqlInterpolatedAsync($"UPDATE platform_settings SET name = {"preserved platform"} WHERE id = 1", ct);
                await previous.Database.MigrateAsync(ct);
                await Assert.That((await previous.PlatformSettings.AsNoTracking().SingleAsync(ct)).Name).IsEqualTo("preserved platform");
                await Assert.That((await previous.PlatformSettings.AsNoTracking().SingleAsync(ct)).PublicGatewayEnabled).IsFalse();
            }
            var policy = new PublicGatewayPolicy(true, "gateway", "https://public.example.test", ["https://direct.example.test"], "203.0.113.1", null, 8);
            var capability = new PublicGatewayCapability("gateway", "runner", [policy.PublicOrigin], 32768, 60999, [], 8, true);
            await using (var write = new NoCtfDbContext(options))
            {
                var store = new PublicGatewayPolicyStore(write, caches, new NoOpTransactionalMessageOutbox(), capability);
                await Assert.That((await store.GetAsync(ct)).Enabled).IsFalse();
                await store.SaveAsync(policy, DateTimeOffset.UtcNow, ct);
                await Assert.That((await store.GetAsync(ct)).Enabled).IsTrue();
            }
            await using (var read = new NoCtfDbContext(options))
            {
                var store = new PublicGatewayPolicyStore(read, caches, new NoOpTransactionalMessageOutbox(), capability);
                await Assert.That((await store.GetAsync(ct)).DirectOrigins).IsEquivalentTo(policy.DirectOrigins);
                var publicBranding = await new PlatformConfigurationStore(read).GetAsync(ct);
                var serialized = System.Text.Json.JsonSerializer.Serialize(publicBranding);
                await Assert.That(serialized.Contains("gateway", StringComparison.OrdinalIgnoreCase)).IsFalse();
                await store.SaveAsync(policy with { Enabled = false }, DateTimeOffset.UtcNow, ct);
                await Assert.That((await store.GetAsync(ct)).PublicOrigin).IsEqualTo(policy.PublicOrigin);
                await Assert.That((await store.GetAsync(ct)).Enabled).IsFalse();
            }
        });
    }
}
