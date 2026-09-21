using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Infrastructure.Runtime.Administration;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Runtime.Targets;
using NoCTF.Infrastructure.Runtime.Placement;
using StackExchange.Redis;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Application.Runtime.Access;
using NoCTF.Infrastructure.Runtime.Access;

namespace NoCTF.Infrastructure.Runtime;

internal static class RuntimeInfrastructure
{
    internal static IServiceCollection AddNoCtfRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        bool development)
    {
        services.AddSingleton(new RuntimeResourceBudgetPolicy());
        var proxyOptions = configuration.GetSection("RuntimeProxy")
            .Get<RuntimeProxyOptions>() ?? new RuntimeProxyOptions();
        if (proxyOptions.MaximumConnectionsPerRuntime is < 1 or > 512
            || proxyOptions.MaximumConnectionMinutes is < 1 or > 1_440
            || proxyOptions.ConnectTimeoutSeconds is < 1 or > 120
            || proxyOptions.BufferSizeBytes is < 4_096 or > 1_048_576
            || proxyOptions.DefaultCaptureLimitBytes
                is < NoCTF.Application.Competitions.Management.RuntimeAccessPolicy.MinimumCaptureLimitBytes
                    or > NoCTF.Application.Competitions.Management.RuntimeAccessPolicy.MaximumCaptureLimitBytes
            || proxyOptions.MaximumCaptureLimitBytes < proxyOptions.DefaultCaptureLimitBytes
            || proxyOptions.MaximumCaptureLimitBytes
                > NoCTF.Application.Competitions.Management.RuntimeAccessPolicy.MaximumCaptureLimitBytes)
        {
            throw new InvalidOperationException("RuntimeProxy configuration is invalid.");
        }
        services.AddSingleton(proxyOptions);
        services.AddOptions<RuntimePlacementOptions>()
            .Configure(options =>
            {
                var providerText = configuration["Runtime:Provider"]
                    ?? configuration["Runner:Provider"];
                if (providerText is not null)
                {
                    options.Provider = Enum.TryParse<RuntimeProvider>(providerText, true, out var provider)
                        ? provider
                        : (RuntimeProvider)(-1);
                }
                options.RunnerPool = configuration["Runtime:RunnerPool"]
                    ?? configuration["Runner:Pool"]
                    ?? options.RunnerPool;
            })
            .Validate(options => options.Provider is RuntimeProvider.Docker
                    or RuntimeProvider.Kubernetes,
                "Runtime:Provider must be Docker or Kubernetes.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.RunnerPool)
                    && options.RunnerPool.Length <= 256,
                "Runtime:RunnerPool must contain 1..256 characters.")
            .ValidateOnStart();
        if (development)
        {
            services.AddOptions<DevelopmentRunnerCapacityOptions>()
                .Bind(configuration.GetSection("Runner"))
                .Validate(options => !string.IsNullOrWhiteSpace(options.Id)
                        && !string.IsNullOrWhiteSpace(options.Pool),
                    "Runner Id and Pool must be configured for development capacity.")
                .ValidateOnStart();
            services.AddSingleton<IRunnerCapacityGate, DevelopmentRunnerCapacityGate>();
        }
        else
        {
            var redis = configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redis))
                throw new InvalidOperationException("ConnectionStrings:Redis is required for the API host.");
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var redisOptions = ConfigurationOptions.Parse(redis);
                redisOptions.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(redisOptions);
            });
            services.AddScoped<RedisRunnerCapacityGate>();
            services.AddScoped<IRunnerCapacityGate, PersistedRunnerCapacityGate>();
        }
        services.AddSingleton<IRuntimePlacementPolicy, ConfiguredRuntimePlacementPolicy>();
        services.AddScoped<IRunnerCapacityDiagnostics>(provider => new RedisRunnerCapacityDiagnostics(
            provider.GetRequiredService<NoCTF.Infrastructure.Persistence.NoCtfDbContext>(),
            provider.GetService<IConnectionMultiplexer>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ObserveRunnerCapacity>();
        services.AddScoped<TeamRuntimeQuota>();
        services.AddScoped<SharedRuntimeCriticalSection>();

        services.AddScoped<IRuntimeInstanceStore, RuntimeInstanceStore>();
        services.AddScoped<IRuntimeProxyTargetReader, RuntimeProxyTargetReader>();
        services.AddScoped<IRuntimeTrafficCaptureFactory, RuntimeTrafficCaptureFactory>();
        services.AddScoped<IRuntimeTrafficCaptureStore, RuntimeTrafficCaptureStore>();
        services.AddScoped<ManageRuntimeTrafficCaptures>();
        services.AddSingleton<IRuntimeProxyConnectionGate, RuntimeProxyConnectionGate>();
        services.AddScoped<GetPlayerRuntime>();
        services.AddScoped<MutatePlayerRuntime>();
        services.AddScoped<IRuntimeTargetReader, RuntimeTargetReader>();
        services.AddScoped<ListRuntimeTargets>();
        services.AddScoped<IAdminRuntimeStore, AdminRuntimeStore>();
        services.AddScoped<ManageAdminRuntimes>();
        return services;
    }
}
