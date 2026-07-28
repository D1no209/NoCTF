using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Runtime.Administration;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Runtime.Targets;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Runtime;

internal static class RuntimeInfrastructure
{
    internal static IServiceCollection AddNoCtfRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
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
        services.AddScoped<IRunnerCapacityGate, RedisRunnerCapacityGate>();

        services.AddScoped<IRuntimeInstanceStore, RuntimeInstanceStore>();
        services.AddScoped<GetPlayerRuntime>();
        services.AddScoped<MutatePlayerRuntime>();
        services.AddScoped<IRuntimeTargetReader, RuntimeTargetReader>();
        services.AddScoped<ListRuntimeTargets>();
        services.AddScoped<IAdminRuntimeStore, AdminRuntimeStore>();
        services.AddScoped<ManageAdminRuntimes>();
        return services;
    }
}
