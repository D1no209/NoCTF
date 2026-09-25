using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AsyncKeyedLock;
using NoCTF.Application.Common;

namespace NoCTF.Infrastructure.Persistence;

internal static class PersistenceInfrastructure
{
    internal static IServiceCollection AddNoCtfPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool exporting,
        bool development)
    {
        services.AddScoped<AggregatePatchPostCommitActions>();
        services.AddScoped<IAtomicAggregatePatch, AggregatePatchTransaction>();
        services.AddSingleton(new AsyncKeyedLocker<string>(
            options => options.PoolSize = 20,
            StringComparer.Ordinal));
        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<NoCtfDbContext>)))
        {
            throw new InvalidOperationException(
                "A relational database provider must be registered before NoCTF infrastructure.");
        }
        return services;
    }
}
