using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Observability;

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
            services.AddScoped<IUserAccountAdministrationStore, OpenApiUserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, OpenApiPlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, OpenApiPlatformLogReader>();
            services.AddScoped<IPlatformAuditLogStore, OpenApiPlatformAuditLogStore>();
        }
        else
        {
            var postgres = configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
            services.AddSingleton(_ => new WolverineProcessDeadLetters(postgres));
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, UserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, PlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, RedisPlatformLogStore>();
            services.AddScoped<IPlatformAuditLogStore, PlatformAuditLogStore>();
        }

        services.AddScoped<ManagePlatform>();
        services.AddScoped<ManageUserAccounts>();
        services.AddScoped<ManagePlatformConfiguration>();
        services.AddScoped<ObservePlatform>();
        return services;
    }
}
