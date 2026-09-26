using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

namespace NoCTF.Infrastructure.Caching;

public static class NoCtfCacheNames
{
    public const string Leaderboards = "leaderboards";
    public const string ReadModels = "read-models";
    public const string LocalComputation = "local-computation";
    public const string WebhookTestStatuses = "webhook-test-statuses";
}

public static class CachingInfrastructure
{
    public static IServiceCollection AddNoCtfCaching(
        this IServiceCollection services,
        IConfiguration configuration,
        bool development)
    {
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(DistributedCacheRegistration)))
            return services;

        services.AddSingleton<DistributedCacheRegistration>();
        services.AddNoCtfLocalComputationCaching(configuration);

        var leaderboard = services.AddFusionCache(NoCtfCacheNames.Leaderboards)
            .WithOptions(options => options.CacheKeyPrefix = "noctf:v3:leaderboard:")
            .WithDefaultEntryOptions(options => options.Duration = TimeSpan.FromDays(3650));
        var readModels = services.AddFusionCache(NoCtfCacheNames.ReadModels)
            .WithOptions(options => options.CacheKeyPrefix = "noctf:v3:read-models:")
            .WithDefaultEntryOptions(options =>
                options.Duration = TimeSpan.FromSeconds(Math.Max(
                    5,
                    configuration.GetValue("Caching:ReadModelsTtlSeconds", 60))));
        var webhookStatuses = services.AddFusionCache(NoCtfCacheNames.WebhookTestStatuses)
            .WithOptions(options => options.CacheKeyPrefix = "noctf:v3:webhook-test:")
            .WithDefaultEntryOptions(options => options.Duration = TimeSpan.FromMinutes(10));
        if (development)
            return services;

        var redis = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Redis is required for distributed caching.");
        services.AddStackExchangeRedisCache(options => options.Configuration = redis);
        leaderboard
            .WithRegisteredDistributedCache()
            .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
            {
                Configuration = redis
            }));
        readModels
            .WithRegisteredDistributedCache()
            .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
            {
                Configuration = redis
            }));
        webhookStatuses
            .WithRegisteredDistributedCache()
            .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
            {
                Configuration = redis
            }));
        return services;
    }

    public static IServiceCollection AddNoCtfLocalComputationCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(LocalComputationCacheRegistration)))
            return services;

        services.AddSingleton<LocalComputationCacheRegistration>();
        services.AddFusionCacheSystemTextJsonSerializer(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        services.AddFusionCache(NoCtfCacheNames.LocalComputation)
            .WithOptions(options => options.CacheKeyPrefix = "noctf:local-computation:")
            .WithDefaultEntryOptions(options =>
                options.Duration = TimeSpan.FromMinutes(Math.Max(
                    1,
                    configuration.GetValue("Caching:LocalComputationTtlMinutes", 30))));
        return services;
    }

    private sealed class LocalComputationCacheRegistration;
    private sealed class DistributedCacheRegistration;
}
