using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Admission;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.Infrastructure.Administration;

internal static class AdministrationInfrastructure
{
    internal static IServiceCollection AddNoCtfAdministration(
        this IServiceCollection services,
        bool exporting,
        bool development)
    {
        if (exporting)
        {
            services.AddScoped<IPlatformAdministrationStore, NoOpPlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, NoOpUserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, NoOpPlatformConfigurationStore>();
            services.AddScoped<IExperimentalFeatureReader, NoOpPlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, NoOpPlatformLogReader>();
            services.AddScoped<IPlatformAuditLogStore, NoOpPlatformAuditLogStore>();
            services.AddScoped<IHumanVerificationConfigurationStore,
                NoOpHumanVerificationConfigurationStore>();
            services.AddScoped<IHumanVerificationConfigurationReader,
                NoOpHumanVerificationConfigurationStore>();
        }
        else if (development)
        {
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, UserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, PlatformConfigurationStore>();
            services.AddScoped<IExperimentalFeatureReader, PlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, NoOpPlatformLogReader>();
            services.AddScoped<IPlatformAuditLogStore, PlatformAuditLogStore>();
            AddHumanVerificationConfigurationStore(services);
        }
        else
        {
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, UserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, PlatformConfigurationStore>();
            services.AddScoped<IExperimentalFeatureReader, PlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, RedisPlatformLogStore>();
            services.AddScoped<IPlatformAuditLogStore, PlatformAuditLogStore>();
            AddHumanVerificationConfigurationStore(services);
        }

        services.AddSingleton(new HumanVerificationValidationPolicy(
            development || exporting));
        services.AddScoped<ManagePlatform>();
        services.AddScoped<ManageUserAccounts>();
        services.AddScoped<ManagePlatformConfiguration>();
        services.AddScoped<ManageHumanVerificationConfiguration>();
        services.AddScoped<ObservePlatform>();
        services.AddScoped<ExportPlatformLogs>();
        return services;
    }

    private static void AddHumanVerificationConfigurationStore(
        IServiceCollection services)
    {
        services.AddScoped<IHumanVerificationConfigurationStore,
            HumanVerificationConfigurationStore>();
        services.AddScoped<IHumanVerificationConfigurationReader,
            HumanVerificationConfigurationStore>();
    }

}
