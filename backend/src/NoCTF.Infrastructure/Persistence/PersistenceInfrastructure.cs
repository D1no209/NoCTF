using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AsyncKeyedLock;
using Wolverine.EntityFrameworkCore;
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
        if (exporting || development)
        {
            var databaseName = exporting
                ? "noctf-openapi"
                : configuration["Development:DatabaseName"] ?? "noctf-development";
            services.AddDbContext<NoCtfDbContext>(options =>
                options.UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(warnings => warnings.Ignore(
                        InMemoryEventId.TransactionIgnoredWarning)),
                contextLifetime: ServiceLifetime.Scoped,
                optionsLifetime: ServiceLifetime.Singleton);
            return services;
        }

        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
            options => options.UseNpgsql(postgres).UseSnakeCaseNamingConvention());
        return services;
    }
}
