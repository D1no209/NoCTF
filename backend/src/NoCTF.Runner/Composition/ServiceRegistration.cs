using Amazon.S3;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.PatchUploads;

using NoCTF.Infrastructure.Storage;
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
using StackExchange.Redis;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Composition;

public sealed class RunnerProgramMarker;

public static class ServiceRegistration
{
    public static IServiceCollection AddNoCtfRunner(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        var configuredProvider = configuration["Runner:Provider"];
        var provider = Enum.TryParse<RuntimeProvider>(
            configuredProvider,
            ignoreCase: true,
            out var parsedProvider)
            ? parsedProvider
            : (RuntimeProvider?)null;
        services.AddOptions<RunnerAvailabilityOptions>()
            .Configure(options =>
            {
                options.RunnerId = configuration["Runner:Id"] ?? string.Empty;
                options.RunnerPool = configuration["Runner:Pool"] ?? string.Empty;
                options.Provider = provider;
                options.MemoryBytes = ReadLongOrZero(configuration, "Runner:Capacity:MemoryBytes");
                options.NanoCpus = ReadLongOrZero(configuration, "Runner:Capacity:NanoCpus");
                options.PidsLimit = ReadLongOrZero(configuration, "Runner:Capacity:PidsLimit");
                options.HeartbeatIntervalSeconds = ReadIntOrZero(
                    configuration,
                    "Runner:Heartbeat:IntervalSeconds");
                options.HeartbeatTtlSeconds = ReadIntOrZero(
                    configuration,
                    "Runner:Heartbeat:TtlSeconds");
            })
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RunnerId)
                    && options.RunnerId.Length <= 128,
                "Runner:Id must contain 1..128 characters.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RunnerPool)
                    && options.RunnerPool.Length <= 256,
                "Runner:Pool must contain 1..256 characters.")
            .Validate(
                options => options.Provider is not null,
                "Runner:Provider must be Docker, Kubernetes, or Libvirt.")
            .Validate(
                options => options.MemoryBytes > 0
                    && options.NanoCpus > 0
                    && options.PidsLimit > 0,
                "Runner capacity values must be positive integers.")
            .Validate(
                options => options.HeartbeatIntervalSeconds > 0
                    && options.HeartbeatTtlSeconds > options.HeartbeatIntervalSeconds,
                "Runner heartbeat TTL must be greater than its positive interval.")
            .ValidateOnStart();
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
            throw new InvalidOperationException("ConnectionStrings:Redis is required for the Runner host.");
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redisOptions = ConfigurationOptions.Parse(redis);
            redisOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisOptions);
        });
        services.AddScoped<IRunnerCapacityGate, RedisRunnerCapacityGate>();
        services.AddSingleton<RedisRunnerAvailabilityRegistry>();
        services.AddHostedService<RunnerAvailabilityPublisher>();
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
            configuration["Runtime:Docker:CallbackContainer"] ?? "noctf-awdp-callback",
            configuration["Runtime:Docker:CallbackContainerLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Docker:CallbackContainerLabelValue"] ?? "awdp-callback-gateway");
        services.AddSingleton(options);
        services.AddSingleton<DockerContainerLifecycle>();
        services.AddSingleton<IRuntimeResourceReaper>(provider =>
            provider.GetRequiredService<DockerContainerLifecycle>());
        services.AddSingleton<DockerComposeRuntime>();
        services.AddSingleton<DockerRuntimeResourceReconciler>();
        services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
            provider.GetRequiredService<DockerRuntimeResourceReconciler>());
        services.AddSingleton(new KubernetesRuntimeOptions(
            configuration["Runtime:Kubernetes:Namespace"] ?? "noctf",
            configuration["Runtime:Kubernetes:PublicHost"] ?? "localhost",
            configuration["Runtime:Kubernetes:ImagePullPolicy"] ?? "IfNotPresent",
            configuration["Runtime:Kubernetes:CallbackPodLabelKey"] ?? "noctf.io/internal-role",
            configuration["Runtime:Kubernetes:CallbackPodLabelValue"] ?? "awdp-callback",
            isKubernetesPool
                ? ReadRequiredPositiveLong(configuration, "Runtime:Kubernetes:PodPidsLimit")
                : 0,
            isKubernetesPool
                ? ReadRequiredString(configuration, "Runtime:Kubernetes:ClusterDomain")
                : string.Empty,
            isKubernetesPool
                ? KubernetesEgressPolicy.ValidateClusterDnsServiceAddress(
                    ReadRequiredString(
                        configuration,
                        "Runtime:Kubernetes:ClusterDnsServiceAddress"))
                : string.Empty,
            isKubernetesPool
                && ReadRequiredTrue(configuration, "Runtime:Kubernetes:NetworkPolicyRequired"),
            isKubernetesPool
                ? KubernetesEgressPolicy.ValidateAndNormalizeProtectedCidrs(
                    configuration.GetSection("Runtime:Kubernetes:ProtectedCidrs")
                        .GetChildren()
                        .Select(section => section.Value ?? string.Empty))
                : null));
        services.AddSingleton<IKubernetes>(_ =>
            new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()));
        if (isKubernetesPool)
            services.AddHostedService<KubernetesRuntimePoolStartupCheck>();
        services.AddSingleton<KubernetesContainerLifecycle>();
        services.AddSingleton<IRuntimeResourceReaper>(provider =>
            provider.GetRequiredService<KubernetesContainerLifecycle>());
        services.AddSingleton<IKomposeConverter>(new KomposeConverter());
        services.AddSingleton<KubernetesComposeRuntime>();
        services.AddSingleton<KubernetesRuntimeResourceReconciler>();
        services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
            provider.GetRequiredService<KubernetesRuntimeResourceReconciler>());
        var hasLibvirtConfiguration =
            !string.IsNullOrWhiteSpace(configuration["Runtime:Libvirt:PoolRoutedNetworkCidr"]);
        if (isLibvirtPool || hasLibvirtConfiguration)
        {
            services.AddSingleton(new LibvirtRuntimeOptions(
                ReadRequiredString(configuration, "Runtime:Libvirt:CacheDirectory"),
                ReadRequiredString(configuration, "Runtime:Libvirt:WorkDirectory"),
                ReadRequiredString(configuration, "Runtime:Libvirt:PoolRoutedNetworkCidr"),
                ReadRequiredString(configuration, "Runtime:Libvirt:NodeRoutedNetworkCidr"),
                ReadRequiredPositiveInt(
                    configuration,
                    "Runtime:Libvirt:RuntimeSubnetPrefixLength")));
            services.AddSingleton<ILibvirtProcessAdapter, LibvirtProcessAdapter>();
            services.AddHttpClient<OvaArtifactCache>();
            services.AddSingleton<LibvirtRoutedNetworkManager>();
            services.AddSingleton<LibvirtApplianceLifecycle>();
            services.AddSingleton<IOvaRuntime>(provider =>
                provider.GetRequiredService<LibvirtApplianceLifecycle>());
            services.AddSingleton<LibvirtRuntimeResourceReconciler>();
            services.AddSingleton<IRuntimeManagedResourceReconciler>(provider =>
                provider.GetRequiredService<LibvirtRuntimeResourceReconciler>());
        }
        services.AddSingleton<RuntimeProviderCatalog>();
        services.AddSingleton<IOneShotRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton<IContainerRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton<IRuntimeProviderCatalog>(provider =>
            provider.GetRequiredService<RuntimeProviderCatalog>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AwdFlagInjectionConfigurationCatalog>();
        services.AddSingleton<AwdCheckerConfigurationCatalog>();
        services.AddSingleton<IRunnerScoringTokenIssuer, RunnerScoringTokenIssuer>();
        services.AddSingleton<IAwdFlagInjectionExecutor, AwdFlagInjectionExecutor>();
        services.AddSingleton<IAwdFlagInjectionWorkReader, AwdFlagInjectionWorkReader>();
        services.AddSingleton<IAwdCheckerWorkReader, AwdCheckerWorkReader>();
        services.AddSingleton<IAwdCheckerExecutor, AwdCheckerExecutor>();
        services.AddSingleton<IAwdpFixWorkReader, AwdpFixWorkReader>();
        services.AddSingleton<IAwdpCheckerExecutor, AwdpCheckerExecutor>();
        services.AddSingleton<FixArchivePreparer>();
        if (string.Equals(configuration["Storage:Provider"], "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(new AmazonS3Config
            {
                ServiceURL = configuration["Storage:S3:ServiceUrl"],
                ForcePathStyle = configuration.GetValue("Storage:S3:ForcePathStyle", true)
            }));
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        }
        else
        {
            services.AddSingleton<IObjectStorage, LocalObjectStorage>();
        }
        services.AddSingleton<IRuntimeNodeWorkReader, RuntimeNodeWorkReader>();
        return services;
    }

    private static long ReadRequiredPositiveLong(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];
        if (!long.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
            || parsed <= 0)
            throw new InvalidOperationException(
                $"{key} must be configured as a positive integer.");
        return parsed;
    }

    private static long ReadPositiveLongOrDefault(
        IConfiguration configuration,
        string key,
        long defaultValue)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        if (!long.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
            || parsed <= 0)
            throw new InvalidOperationException(
                $"{key} must be configured as a positive integer.");
        return parsed;
    }

    private static int ReadRequiredPositiveInt(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];
        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
            || parsed <= 0)
            throw new InvalidOperationException(
                $"{key} must be configured as a positive integer.");
        return parsed;
    }

    private static string ReadRequiredString(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} must be configured.");
        return value;
    }

    private static bool ReadRequiredTrue(
        IConfiguration configuration,
        string key)
    {
        if (!bool.TryParse(configuration[key], out var value) || !value)
            throw new InvalidOperationException($"{key} must be configured as true.");
        return true;
    }

    private static long ReadLongOrZero(IConfiguration configuration, string key) =>
        long.TryParse(
            configuration[key],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private static int ReadIntOrZero(IConfiguration configuration, string key) =>
        int.TryParse(
            configuration[key],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
}
