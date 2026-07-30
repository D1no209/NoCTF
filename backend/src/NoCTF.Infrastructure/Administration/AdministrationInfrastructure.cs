using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Administration;

namespace NoCTF.Infrastructure.Administration;

internal static class AdministrationInfrastructure
{
    internal static IServiceCollection AddNoCtfAdministration(
        this IServiceCollection services,
        IConfiguration configuration,
        bool exporting)
    {
        if (exporting)
        {
            services.AddScoped<IPlatformAdministrationStore, OpenApiPlatformAdministrationStore>();
        }
        else
        {
            var postgres = configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
            services.AddSingleton(_ => new WolverineProcessDeadLetters(postgres));
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
        }

        services.AddScoped<ManagePlatform>();
        return services;
    }
}
