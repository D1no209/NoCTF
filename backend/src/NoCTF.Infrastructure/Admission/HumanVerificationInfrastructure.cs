using Microsoft.Extensions.DependencyInjection;
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
        bool development)
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
        services.AddHttpClient(CapTelemetryReader.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 128 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddSingleton<ICapTelemetryReader, CapTelemetryReader>();
        services.AddScoped<ManageCapWorkloadConfiguration>();
        services.AddHttpClient(CapHumanVerificationConfigurationProbe.ClientName,
                client =>
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.MaxResponseContentBufferSize = 16 * 1024;
                })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        services.AddSingleton(new CapHumanVerificationValidationOptions(
            configuration["HumanVerification:Validation:PublicOrigin"]
                ?? string.Empty,
            TimeSpan.FromSeconds(Math.Max(
                1,
                configuration.GetValue(
                    "HumanVerification:Validation:StageTimeoutSeconds", 3))),
            development));
        services.AddSingleton<ICapHumanVerificationConfigurationProbe,
            CapHumanVerificationConfigurationProbe>();
        return services;
    }
}
