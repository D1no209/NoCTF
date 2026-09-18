using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Kubernetes.Compose;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runtime.Kubernetes;
using NoCTF.Runtime.Libvirt;
using k8s;
using NoCTF.Runner.Messages;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Application.Authentication;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runtime.Kubernetes.Networking;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using StackExchange.Redis;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Caching;
using NoCTF.Application.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Hosting.Health;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Infrastructure.GameplayFacts.Processing;

namespace NoCTF.Runner.Composition;

public sealed class RunnerProgramMarker;

public static class ServiceRegistration
{
    public static IServiceCollection AddNoCtfRunner(
        this IServiceCollection services,
        IConfiguration configuration,
        bool development = false)
    {
        var callbackBaseUrl = configuration
            .GetSection(RunnerScoringOptions.SectionName)
            .Get<RunnerScoringOptions>()?
            .CallbackBaseUrl;
        if (callbackBaseUrl is null
            || callbackBaseUrl.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.");
        }
        services.AddOptions<RunnerScoringOptions>()
            .Bind(configuration.GetSection(RunnerScoringOptions.SectionName))
            .Validate(options => System.Text.Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "RunnerScoring:SigningKey must contain at least 32 UTF-8 bytes.")
            .Validate(options => options.CallbackBaseUrl is not null,
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.")
            .ValidateOnStart();
        services.AddScoped<ICompetitionEventRecorder, CompetitionEventStore>();
        services.AddScoped<IAwdpFixExecutionFence, PostgresAwdpFixExecutionFence>();
        services.AddNoCtfLocalComputationCaching(configuration);
        services.AddHttpClient();
        services.AddHttpClient(
                AwdpFixArchiveDownloader.ClientName,
                client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            })
            .AddResilienceHandler("awdp-archive", pipeline =>
            {
                var retry = CreateGetRetry(maxRetryAttempts: 2);
                pipeline.AddRetry(retry);
            });
        var configuredProvider = configuration["Runner:Provider"];
        services.AddSingleton<IValidateOptions<RunnerOptions>, RunnerOptionsValidator>();
        services.AddOptions<RunnerOptions>()
            .Bind(configuration.GetSection(RunnerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FixVerificationOptions>,
            FixVerificationOptionsValidator>();
        services.AddOptions<FixVerificationOptions>()
            .Bind(configuration.GetSection(FixVerificationOptions.SectionName))
            .ValidateOnStart();
        if (!development)
        {
            var redis = configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redis))
                throw new InvalidOperationException(
                    "ConnectionStrings:Redis is required for the Runner host.");
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var redisOptions = ConfigurationOptions.Parse(redis);
                redisOptions.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(redisOptions);
            });
            services.AddScoped<RedisRunnerCapacityGate>();
            services.AddScoped<IRunnerCapacityGate, PersistedRunnerCapacityGate>();
            services.AddSingleton<RedisRunnerAvailabilityRegistry>();
            services.AddSingleton<RedisRunnerCapacityLedger>();
            services.AddHostedService<RunnerAvailabilityPublisher>();
        }
        services.AddSingleton<RunnerResourceMutationCoordinator>();
        if (!development)
            services.AddScoped<AuxiliaryRuntimeCapacity>();
        var isKubernetesPool = string.Equals(
            configuredProvider,
            nameof(NoCTF.Domain.Runtime.RuntimeProvider.Kubernetes),
            StringComparison.OrdinalIgnoreCase);
        var isLibvirtPool = string.Equals(
            configuredProvider,
            nameof(NoCTF.Domain.Runtime.RuntimeProvider.Libvirt),
            StringComparison.OrdinalIgnoreCase);
        services.AddDockerRuntimeProvider(configuration);
        services.AddKubernetesRuntimeProvider(configuration, isKubernetesPool);
        services.AddLibvirtRuntimeProvider(configuration, isLibvirtPool);
        services.AddSingleton<RuntimeProviderCatalog>();
        services.AddSingleton<RuntimeProviderAvailabilityCatalog>();
        services.AddSingleton<RunnerProviderHealthState>();
        services.AddSingleton<IReadinessDependency, RunnerProviderReadinessDependency>();
        services.AddSingleton<IOneShotRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton<IContainerRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton<IRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AwdFlagInjectionConfigurationCatalog>();
        services.AddSingleton<IAwdFlagInjectionConfigurationCatalog,
            FusionAwdFlagInjectionConfigurationCatalog>();
        services.AddSingleton<AwdCheckerConfigurationCatalog>();
        services.AddSingleton<IRunnerScoringTokenIssuer, RunnerScoringTokenIssuer>();
        services.AddSingleton<IAwdFlagInjectionExecutor, AwdFlagInjectionExecutor>();
        services.AddSingleton<IAwdFlagInjectionWorkReader, AwdFlagInjectionWorkReader>();
        services.AddSingleton<IChallengeTestFlagInjectionStore, ChallengeTestFlagInjectionStore>();
        services.AddSingleton<IAwdCheckerWorkReader, AwdCheckerWorkReader>();
        services.AddScoped<RuntimeProviderHandler>();
        services.AddScoped<ContainerRuntimeMessageHandler>();
        services.AddScoped<ComposeRuntimeMessageHandler>();
        services.AddScoped<OvaRuntimeMessageHandler>();
        services.AddScoped<RuntimeTerminationMessageHandler>();
        services.AddScoped<RuntimeProvisionWriteBackMessageHandler>();
        services.AddScoped<RuntimeStopWriteBackMessageHandler>();
        services.AddScoped<IAwdCheckerExecutor, AwdCheckerExecutor>();
        services.AddSingleton<IAwdpFixWorkReader, AwdpFixWorkReader>();
        services.AddScoped<IAwdpCheckerExecutor, AwdpCheckerExecutor>();
        services.AddSingleton<IAwdpAttackProvisioningPlanReader,
            AwdpAttackProvisioningPlanReader>();
        services.AddSingleton<AwdpFixArchiveDownloader>();
        services.AddSingleton<FixArchivePreparer>();
        services.AddSingleton<IRuntimeNodeWorkReader, RuntimeNodeWorkReader>();
        NoCTF.Runner.PublicAccess.PublicGatewayRegistration.AddPublicGatewayCoordinator(services, configuration);
        return services;
    }

    internal static HttpRetryStrategyOptions CreateGetRetry(int maxRetryAttempts)
    {
        var retry = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = maxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(100),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true
        };
        retry.DisableForUnsafeHttpMethods();
        return retry;
    }

}
