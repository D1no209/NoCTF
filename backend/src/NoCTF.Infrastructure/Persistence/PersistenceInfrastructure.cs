using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Persistence;

internal static class PersistenceInfrastructure
{
    internal static IServiceCollection AddNoCtfPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool exporting)
    {
        if (exporting)
        {
            services.AddDbContext<NoCtfDbContext>(options =>
                options.UseInMemoryDatabase("noctf-openapi"));
            return services;
        }

        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
            options => options.UseNpgsql(postgres).UseSnakeCaseNamingConvention());
        return services;
    }
}
