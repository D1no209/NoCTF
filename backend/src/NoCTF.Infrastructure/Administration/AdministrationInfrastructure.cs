using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Challenges.Bank;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.Monitoring;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Admission;
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
        services.AddSingleton(CreateMonitoringThresholds(configuration));

        if (exporting)
        {
            services.AddScoped<IPlatformAdministrationStore, NoOpPlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, NoOpUserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, NoOpPlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, NoOpPlatformLogReader>();
            services.AddScoped<IPlatformAuditLogStore, NoOpPlatformAuditLogStore>();
            services.AddSingleton<IPlatformMonitoringReader,
                NoOpPlatformMonitoringReader>();
            services.AddScoped<NoOpHumanVerificationConfigurationStore>();
            services.AddScoped<IHumanVerificationConfigurationStore>(provider =>
                provider.GetRequiredService<NoOpHumanVerificationConfigurationStore>());
            services.AddScoped<IHumanVerificationConfigurationReader>(provider =>
                provider.GetRequiredService<NoOpHumanVerificationConfigurationStore>());
        }
        else if (development)
        {
            services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
            services.AddScoped<IUserAccountAdministrationStore, UserAccountAdministrationStore>();
            services.AddScoped<IPlatformConfigurationStore, PlatformConfigurationStore>();
            services.AddSingleton<IPlatformLogReader, NoOpPlatformLogReader>();
            services.AddScoped<IPlatformAuditLogStore, PlatformAuditLogStore>();
            services.AddSingleton<IPlatformMonitoringReader,
                UnavailablePlatformMonitoringReader>();
            AddHumanVerificationConfigurationStore(services);
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
            AddHumanVerificationConfigurationStore(services);
        }

        services.AddSingleton(new HumanVerificationValidationPolicy(
            development || exporting));
        services.AddScoped<ManagePlatform>();
        services.AddScoped<ManageUserAccounts>();
        services.AddScoped<ManagePlatformConfiguration>();
        services.AddScoped<IExperimentalFeatureReader>(provider =>
            (IExperimentalFeatureReader)provider.GetRequiredService<IPlatformConfigurationStore>());
        services.AddScoped<ManageHumanVerificationConfiguration>();
        services.AddScoped<ObservePlatform>();
        services.AddScoped<ExportPlatformLogs>();
        services.AddScoped<ObservePlatformMonitoring>();
        return services;
    }

    private static void AddHumanVerificationConfigurationStore(
        IServiceCollection services)
    {
        services.AddScoped<HumanVerificationConfigurationStore>();
        services.AddScoped<IHumanVerificationConfigurationStore>(provider =>
            provider.GetRequiredService<HumanVerificationConfigurationStore>());
        services.AddScoped<IHumanVerificationConfigurationReader>(provider =>
            provider.GetRequiredService<HumanVerificationConfigurationStore>());
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

    private static PlatformMonitoringThresholds CreateMonitoringThresholds(
        IConfiguration configuration)
    {
        const string prefix = "Observability:Monitoring";
        var defaults = PlatformMonitoringThresholds.Default;
        var thresholds = new PlatformMonitoringThresholds(
            PositiveInt(configuration, $"{prefix}:SustainedWindowMinutes",
                defaults.SustainedWindowMinutes),
            NonNegativeInt(configuration, $"{prefix}:PendingWarning",
                defaults.PendingWarning),
            PositiveInt(configuration, $"{prefix}:PendingCritical",
                defaults.PendingCritical),
            NonNegativeInt(configuration, $"{prefix}:AckPendingWarning",
                defaults.AckPendingWarning),
            PositiveInt(configuration, $"{prefix}:AckPendingCritical",
                defaults.AckPendingCritical),
            NonNegativeInt(configuration, $"{prefix}:RedeliveryWarning",
                defaults.RedeliveryWarning),
            PositiveInt(configuration, $"{prefix}:RedeliveryCritical",
                defaults.RedeliveryCritical),
            NonNegativeInt(configuration, $"{prefix}:OutboxWarning",
                defaults.OutboxWarning),
            PositiveInt(configuration, $"{prefix}:OutboxCritical",
                defaults.OutboxCritical),
            NonNegativeInt(configuration, $"{prefix}:InboxWarning",
                defaults.InboxWarning),
            PositiveInt(configuration, $"{prefix}:InboxCritical",
                defaults.InboxCritical),
            Percentage(configuration, $"{prefix}:JetStreamStorageWarningPercent",
                defaults.JetStreamStorageWarningPercent),
            Percentage(configuration, $"{prefix}:JetStreamStorageCriticalPercent",
                defaults.JetStreamStorageCriticalPercent),
            PositiveInt(configuration, $"{prefix}:LatencyMinimumSamples", defaults.LatencyMinimumSamples),
            PositiveInt(configuration, $"{prefix}:LatencySustainedWindowMinutes", defaults.LatencySustainedWindowMinutes));

        ValidatePair(thresholds.PendingWarning, thresholds.PendingCritical,
            "pending");
        if (thresholds.LatencySustainedWindowMinutes > 60)
            throw new InvalidOperationException("LatencySustainedWindowMinutes must be between 1 and 60.");
        ValidatePair(thresholds.AckPendingWarning, thresholds.AckPendingCritical,
            "ack pending");
        ValidatePair(thresholds.RedeliveryWarning, thresholds.RedeliveryCritical,
            "redelivery");
        ValidatePair(thresholds.OutboxWarning, thresholds.OutboxCritical,
            "outbox");
        ValidatePair(thresholds.InboxWarning, thresholds.InboxCritical,
            "inbox");
        if (thresholds.JetStreamStorageWarningPercent
            >= thresholds.JetStreamStorageCriticalPercent)
        {
            throw new InvalidOperationException(
                "JetStream storage warning threshold must be below its critical threshold.");
        }
        return thresholds;
    }

    private static int PositiveInt(
        IConfiguration configuration,
        string key,
        int fallback) => ParseInt(configuration, key, fallback, minimum: 1);

    private static int NonNegativeInt(
        IConfiguration configuration,
        string key,
        int fallback) => ParseInt(configuration, key, fallback, minimum: 0);

    private static int ParseInt(
        IConfiguration configuration,
        string key,
        int fallback,
        int minimum)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var value)
            || value < minimum)
        {
            throw new InvalidOperationException(
                $"{key} must be an integer greater than or equal to {minimum}.");
        }
        return value;
    }

    private static double Percentage(
        IConfiguration configuration,
        string key,
        double fallback)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var value)
            || value is < 0 or > 100)
        {
            throw new InvalidOperationException(
                $"{key} must be a percentage between 0 and 100.");
        }
        return value;
    }

    private static void ValidatePair(int warning, int critical, string name)
    {
        if (warning >= critical)
        {
            throw new InvalidOperationException(
                $"The {name} warning threshold must be below its critical threshold.");
        }
    }
}
