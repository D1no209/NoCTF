using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Infrastructure.Competitions.Webhooks;
using NoCTF.Infrastructure.Caching;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[Category("CompetitionWebhooks")]
public sealed class CompetitionWebhookTestStatusTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Test_delivery_status_is_temporary_and_bound_to_its_identifiers(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(cancellationToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFusionCacheSystemTextJsonSerializer(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            services.AddStackExchangeRedisCache(options =>
                options.Configuration = container.GetConnectionString());
            services.AddFusionCache(NoCtfCacheNames.WebhookTestStatuses)
                .WithDefaultEntryOptions(options => options.Duration = TimeSpan.FromMinutes(10))
                .WithRegisteredDistributedCache()
                .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
                {
                    Configuration = container.GetConnectionString()
                }));
            using var provider = services.BuildServiceProvider();
            var store = new FusionCompetitionWebhookTestStatusStore(
                provider.GetRequiredService<IFusionCacheProvider>());
            var now = DateTimeOffset.UtcNow;
            var status = new CompetitionWebhookTestStatus(
                Guid.CreateVersion7(now),
                Guid.CreateVersion7(now.AddTicks(1)),
                Guid.CreateVersion7(now.AddTicks(2)),
                CompetitionWebhookTestState.Pending,
                now);

            await store.CreateAsync(status, cancellationToken);
            await Assert.That(await store.GetAsync(status.DeliveryId, cancellationToken))
                .IsEqualTo(status);

            await store.CompleteAsync(
                status.DeliveryId,
                CompetitionWebhookTestState.Succeeded,
                now.AddSeconds(1),
                null,
                cancellationToken);
            var completed = await store.GetAsync(status.DeliveryId, cancellationToken);
            await Assert.That(completed!.State)
                .IsEqualTo(CompetitionWebhookTestState.Succeeded);
            await Assert.That(completed.CompletedAt).IsEqualTo(now.AddSeconds(1));
        });
    }
}
