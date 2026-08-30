using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
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
                            client.Timeout = Timeout.InfiniteTimeSpan;
                        })
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    })
                    .AddStandardResilienceHandler(options =>
                    {
                        options.Retry.MaxRetryAttempts = 2;
                        options.Retry.Delay = TimeSpan.FromMilliseconds(100);
                        options.Retry.BackoffType = DelayBackoffType.Exponential;
                        options.Retry.UseJitter = true;
                        options.Retry.DisableForUnsafeHttpMethods();
                        options.CircuitBreaker.FailureRatio = 0.5;
                        options.CircuitBreaker.MinimumThroughput = 4;
                        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(5);
                        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
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
