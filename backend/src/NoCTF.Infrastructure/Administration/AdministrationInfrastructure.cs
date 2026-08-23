using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.Monitoring;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Administration.Monitoring;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.Infrastructure.Administration;

internal static class AdministrationInfrastructure
{
    internal static IServiceCollection AddNoCtfAdministration(
        this IServiceCollection services,
        IConfiguration configuration,
        bool exporting,
        bool development)
    {
        var dashboardUri = OptionalHttpUri(
            configuration["Observability:GrafanaPublicUrl"],
            "Observability:GrafanaPublicUrl");
        services.AddSingleton(new PlatformMonitoringOptions(dashboardUri));

        if (exporting)
        {
            services.AddScoped<IPlatformAdministrationStore, OpenApiPlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, OpenApiUserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, OpenApiPlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, OpenApiPlatformLogReader>();
            services.AddScoped<IPlatformAuditLogStore, OpenApiPlatformAuditLogStore>();
            services.AddSingleton<IPlatformMonitoringReader,
                OpenApiPlatformMonitoringReader>();
        }
        else if (development)
        {
            services.AddSingleton<IProcessDeadLetterStore, DevelopmentProcessDeadLetterStore>();
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, UserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, PlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, OpenApiPlatformLogReader>();
            services.AddScoped<IPlatformAuditLogStore, PlatformAuditLogStore>();
            services.AddSingleton<IPlatformMonitoringReader,
                UnavailablePlatformMonitoringReader>();
        }
        else
        {
            var postgres = configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
            services.AddSingleton<IProcessDeadLetterStore>(_ =>
                new WolverineProcessDeadLetters(postgres));
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, UserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, PlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, RedisPlatformLogStore>();
            services.AddScoped<IPlatformAuditLogStore, PlatformAuditLogStore>();
            var prometheusUri = OptionalHttpUri(
                configuration["Observability:PrometheusBaseUrl"],
                "Observability:PrometheusBaseUrl");
            if (prometheusUri is null)
            {
                services.AddSingleton<IPlatformMonitoringReader,
                    UnavailablePlatformMonitoringReader>();
            }
            else
            {
                services.AddHttpClient(PrometheusPlatformMonitoringReader.ClientName,
                        client =>
                        {
                            client.BaseAddress = NormalizeBaseUri(prometheusUri);
                            client.Timeout = TimeSpan.FromSeconds(5);
                        })
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    });
                services.AddSingleton<IPlatformMonitoringReader,
                    PrometheusPlatformMonitoringReader>();
            }
        }

        services.AddScoped<ManagePlatform>();
        services.AddScoped<ManageUserAccounts>();
        services.AddScoped<ManagePlatformConfiguration>();
        services.AddScoped<ObservePlatform>();
        services.AddScoped<ExportPlatformLogs>();
        services.AddScoped<ObservePlatformMonitoring>();
        return services;
    }

    private static Uri? OptionalHttpUri(string? value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"{settingName} must be an absolute HTTP or HTTPS URL.");
        }
        return uri;
    }

    private static Uri NormalizeBaseUri(Uri uri) =>
        new(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
}
