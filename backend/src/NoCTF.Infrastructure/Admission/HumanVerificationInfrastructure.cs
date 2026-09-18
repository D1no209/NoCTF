using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using NoCTF.Application.Admission;
using Polly;

namespace NoCTF.Infrastructure.Admission;

public static class HumanVerificationInfrastructure
{
    public static IServiceCollection AddNoCtfHumanVerification(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableMonitoring)
    {
        services.AddHttpClient(HttpHumanVerificationVerifier.ClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = 16 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            })
            .AddResilienceHandler("human-verification", pipeline =>
            {
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 4,
                    SamplingDuration = TimeSpan.FromSeconds(20),
                    BreakDuration = TimeSpan.FromSeconds(10)
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(5));
            });
        services.AddSingleton<IHumanVerificationVerifier, HttpHumanVerificationVerifier>();
        services.AddHttpClient(CapWorkloadConfigurationClient.ClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = 64 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            })
            .AddResilienceHandler("cap-workload-configuration", pipeline =>
            {
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 4,
                    SamplingDuration = TimeSpan.FromSeconds(20),
                    BreakDuration = TimeSpan.FromSeconds(10)
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(5));
            });
        services.AddSingleton<ICapWorkloadConfigurationClient,
            CapWorkloadConfigurationClient>();
        services.AddScoped<ManageCapWorkloadConfiguration>();
        if (enableMonitoring)
        {
            services.AddHttpClient(CapHumanVerificationMonitor.ClientName, client =>
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.MaxResponseContentBufferSize = 16 * 1024;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false
                });
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(new CapHumanVerificationMonitoringOptions(
                configuration["HumanVerification:Monitoring:PublicOrigin"]
                    ?? string.Empty,
                TimeSpan.FromSeconds(Math.Max(
                    5,
                    configuration.GetValue(
                        "HumanVerification:Monitoring:IntervalSeconds", 60))),
                TimeSpan.FromSeconds(Math.Max(
                    1,
                    configuration.GetValue(
                        "HumanVerification:Monitoring:StageTimeoutSeconds", 3)))));
            services.AddSingleton<CapHumanVerificationMonitor>();
            services.AddSingleton<IHumanVerificationMonitoringReader>(provider =>
                provider.GetRequiredService<CapHumanVerificationMonitor>());
            services.AddHostedService(provider =>
                provider.GetRequiredService<CapHumanVerificationMonitor>());
        }
        return services;
    }
}
