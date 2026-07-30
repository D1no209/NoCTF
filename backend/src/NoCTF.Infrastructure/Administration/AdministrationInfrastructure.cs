using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.Bots;
using NoCTF.Infrastructure.Administration.Bots;

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
            services.AddScoped<IPlatformBotStore, OpenApiPlatformBotStore>();
        }
        else
        {
            var postgres = configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
            services.AddSingleton(_ => new WolverineProcessDeadLetters(postgres));
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
            services.AddScoped<IPlatformBotStore, PlatformBotStore>();
        }

        services.AddScoped<ManagePlatform>();
        services.AddScoped<CreatePlatformBot>();
        services.AddScoped<IssuePlatformBotToken>();
        return services;
    }
}
