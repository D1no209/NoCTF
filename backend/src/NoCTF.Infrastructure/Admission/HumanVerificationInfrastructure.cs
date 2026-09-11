using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using NoCTF.Application.Admission;
using Polly;

namespace NoCTF.Infrastructure.Admission;

public static class HumanVerificationInfrastructure
{
    public static IServiceCollection AddNoCtfHumanVerification(
        this IServiceCollection services)
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
        return services;
    }
}
