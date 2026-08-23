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
        ValidateRunnerScoringCallbackBaseUrl(configuration);
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
            services.AddScoped<IRunnerCapacityGate, RedisRunnerCapacityGate>();
            services.AddSingleton<RedisRunnerAvailabilityRegistry>();
            services.AddHostedService<RunnerAvailabilityPublisher>();
        }
        var isKubernetesPool = string.Equals(
            configuredProvider,
            nameof(NoCTF.Domain.Runtime.RuntimeProvider.Kubernetes),
            StringComparison.OrdinalIgnoreCase);
        var isLibvirtPool = string.Equals(
            configuredProvider,
            nameof(NoCTF.Domain.Runtime.RuntimeProvider.Libvirt),
            StringComparison.OrdinalIgnoreCase);
        var options = new DockerRuntimeOptions(
            configuration["Runtime:Docker:Endpoint"] ?? "npipe://./pipe/docker_engine",
            configuration["Runtime:Docker:Network"] ?? "noctf",
            configuration["Runtime:Docker:PublicHost"] ?? "localhost",
            configuration["Runtime:Docker:CallbackContainer"] ?? string.Empty,
            configuration["Runtime:Docker:CallbackContainerLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Docker:CallbackContainerLabelValue"] ?? "scoring-callback-gateway",
            configuration.GetValue<long?>("Runtime:Docker:RuntimeLogMaxSizeBytes")
                ?? 10_485_760,
            configuration.GetValue<int?>("Runtime:Docker:RuntimeLogMaxFiles") ?? 3,
            configuration.GetValue<int?>("Runtime:Docker:OneShotOutputLimitBytesPerStream")
                ?? 1_048_576);
        if (options.RuntimeLogMaxSizeBytes <= 0
            || options.RuntimeLogMaxFiles <= 0
            || options.OneShotOutputLimitBytesPerStream <= 0)
            throw new InvalidOperationException(
                "Docker Runtime limits must be configured as positive integers.");
        services.AddSingleton(options);
        services.AddSingleton<DockerContainerLifecycle>();
        services.AddSingleton<DockerComposeRuntime>();
        services.AddSingleton<DockerRuntimeResourceReconciler>();
        services.AddKeyedSingleton<IRuntimeProviderAvailabilityProbe>(
            RuntimeProvider.Docker,
            (provider, _) => provider.GetRequiredService<DockerRuntimeResourceReconciler>());
        services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
            provider.GetRequiredService<DockerRuntimeResourceReconciler>());
        services.AddSingleton(new KubernetesRuntimeOptions(
            configuration["Runtime:Kubernetes:Namespace"] ?? "noctf",
            configuration["Runtime:Kubernetes:PublicHost"] ?? "localhost",
            configuration["Runtime:Kubernetes:ImagePullPolicy"] ?? "IfNotPresent",
            configuration["Runtime:Kubernetes:CallbackPodLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Kubernetes:CallbackPodLabelValue"] ?? "awdp-callback",
            isKubernetesPool
                ? configuration.GetValue<long>("Runtime:Kubernetes:PodPidsLimit")
                : 0,
            isKubernetesPool
                ? configuration["Runtime:Kubernetes:ClusterDomain"] ?? string.Empty
                : string.Empty,
            isKubernetesPool
                ? KubernetesEgressPolicy.ValidateClusterDnsServiceAddress(
                    configuration["Runtime:Kubernetes:ClusterDnsServiceAddress"]
                        ?? string.Empty)
                : string.Empty,
            isKubernetesPool
                && configuration.GetValue<bool>(
                    "Runtime:Kubernetes:NetworkPolicyRequired"),
            isKubernetesPool
                ? KubernetesEgressPolicy.ValidateAndNormalizeProtectedCidrs(
                    configuration.GetSection("Runtime:Kubernetes:ProtectedCidrs")
                        .GetChildren()
                        .Select(section => section.Value ?? string.Empty))
                : null,
            isKubernetesPool
                ? configuration["Runtime:Kubernetes:CallbackNamespaceLabelKey"]
                    ?? string.Empty
                : "kubernetes.io/metadata.name",
            isKubernetesPool
                ? configuration["Runtime:Kubernetes:CallbackNamespaceLabelValue"]
                    ?? string.Empty
                : "noctf"));
        if (isKubernetesPool
            && (configuration.GetValue<long>("Runtime:Kubernetes:PodPidsLimit") <= 0
                || string.IsNullOrWhiteSpace(configuration["Runtime:Kubernetes:ClusterDomain"])
                || string.IsNullOrWhiteSpace(
                    configuration["Runtime:Kubernetes:ClusterDnsServiceAddress"])
                || !configuration.GetValue<bool>(
                    "Runtime:Kubernetes:NetworkPolicyRequired")
                || string.IsNullOrWhiteSpace(
                    configuration["Runtime:Kubernetes:CallbackNamespaceLabelKey"])
                || string.IsNullOrWhiteSpace(
                    configuration["Runtime:Kubernetes:CallbackNamespaceLabelValue"])))
            throw new InvalidOperationException(
                "The active Kubernetes Runtime requires positive PIDs, cluster DNS, network policy, and callback namespace settings.");
        services.AddSingleton<IKubernetes>(_ =>
            new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()));
        if (isKubernetesPool)
            services.AddHostedService<KubernetesRuntimePoolStartupCheck>();
        services.AddSingleton<KubernetesContainerLifecycle>();
        services.AddSingleton<IKomposeConverter>(new KomposeConverter());
        services.AddSingleton<KubernetesComposeRuntime>();
        services.AddSingleton<KubernetesRuntimeResourceReconciler>();
        services.AddKeyedSingleton<IRuntimeProviderAvailabilityProbe>(
            RuntimeProvider.Kubernetes,
            (provider, _) => provider.GetRequiredService<KubernetesRuntimeResourceReconciler>());
        services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
            provider.GetRequiredService<KubernetesRuntimeResourceReconciler>());
        var hasLibvirtConfiguration =
            !string.IsNullOrWhiteSpace(configuration["Runtime:Libvirt:PoolRoutedNetworkCidr"]);
        if (isLibvirtPool || hasLibvirtConfiguration)
        {
            services.AddSingleton(new LibvirtRuntimeOptions(
                configuration["Runtime:Libvirt:CacheDirectory"] ?? string.Empty,
                configuration["Runtime:Libvirt:WorkDirectory"] ?? string.Empty,
                configuration["Runtime:Libvirt:PoolRoutedNetworkCidr"] ?? string.Empty,
                configuration["Runtime:Libvirt:NodeRoutedNetworkCidr"] ?? string.Empty,
                configuration.GetValue<int>(
                    "Runtime:Libvirt:RuntimeSubnetPrefixLength")));
            services.AddSingleton<ILibvirtProcessAdapter, LibvirtProcessAdapter>();
            services.AddHttpClient<OvaArtifactCache>(client =>
                    client.Timeout = Timeout.InfiniteTimeSpan)
                .AddResilienceHandler("ova-artifact", pipeline =>
                    pipeline.AddRetry(CreateGetRetry(maxRetryAttempts: 2)));
            services.AddSingleton<LibvirtRoutedNetworkManager>();
            services.AddSingleton<LibvirtApplianceLifecycle>();
            services.AddSingleton<IOvaRuntime>(provider =>
                provider.GetRequiredService<LibvirtApplianceLifecycle>());
            services.AddSingleton<LibvirtRuntimeResourceReconciler>();
            services.AddKeyedSingleton<IRuntimeProviderAvailabilityProbe>(
                RuntimeProvider.Libvirt,
                (provider, _) => provider.GetRequiredService<LibvirtRuntimeResourceReconciler>());
            services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
                provider.GetRequiredService<LibvirtRuntimeResourceReconciler>());
        }
        services.AddSingleton<RuntimeProviderCatalog>();
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
        services.AddSingleton<IAwdCheckerWorkReader, AwdCheckerWorkReader>();
        services.AddSingleton<IAwdCheckerExecutor, AwdCheckerExecutor>();
        services.AddSingleton<IAwdpFixWorkReader, AwdpFixWorkReader>();
        services.AddSingleton<IAwdpCheckerExecutor, AwdpCheckerExecutor>();
        services.AddSingleton<IAwdpAttackProvisioningPlanReader,
            AwdpAttackProvisioningPlanReader>();
        services.AddSingleton<AwdpFixArchiveDownloader>();
        services.AddSingleton<FixArchivePreparer>();
        services.AddSingleton<IRuntimeNodeWorkReader, RuntimeNodeWorkReader>();
        return services;
    }

    private static void ValidateRunnerScoringCallbackBaseUrl(IConfiguration configuration)
    {
        var value = configuration["RunnerScoring:CallbackBaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.");
    }

    private static HttpRetryStrategyOptions CreateGetRetry(int maxRetryAttempts)
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
