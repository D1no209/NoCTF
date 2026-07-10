using System.Net;
using System.Text;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using NoCTF.PluginBase;

namespace NoCTF.Container.K8s;

public sealed class KubernetesManager(KubernetesProvider provider, ILogger<KubernetesManager> logger) : IContainerManager
{
    private readonly IKubernetes _client = provider.Client;
    private readonly KubernetesRunnerOptions _options = provider.Options;

    public async Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var spec = OrchestrationSpecSerializer.Read(config.OrchestrationJson);
        spec.Image = string.IsNullOrWhiteSpace(spec.Image) ? config.Image : spec.Image;
        spec.Command = string.IsNullOrWhiteSpace(spec.Command) ? config.Command : spec.Command;
        spec.ExposedPort ??= config.PortMappings?.Keys.FirstOrDefault(port => port > 0);
        config = ApplyOrchestrationConfig(config, spec);

        var nameSeed = config.NetworkAliases?.FirstOrDefault(alias => !string.IsNullOrWhiteSpace(alias))
            ?? $"ctf-{Path.GetFileNameWithoutExtension(spec.Image).Split(':')[0]}-{Guid.NewGuid():N}";
        var name = KubernetesNames.SafeName(nameSeed);
        var namespaceName = KubernetesNames.InstanceNamespace(_options.NamespacePrefix, $"{name}-{Guid.NewGuid():N}");

        try
        {
            await PrepareNamespaceAsync(namespaceName, spec, config.Labels, cancellationToken);

            var deployment = KubernetesManifestFactory.Deployment(namespaceName, name, config, _options, spec);
            await _client.AppsV1.CreateNamespacedDeploymentAsync(deployment, namespaceName, cancellationToken: cancellationToken);

            var port = KubernetesManifestFactory.ResolvePort(config, spec);
            Dictionary<int, int> publishedPorts = [];
            string? publicHost = null;
            string? entryUrl = null;
            if (port is > 0)
            {
                var exposure = KubernetesManifestFactory.ResolveExposure(spec, _options);
                var service = await _client.CoreV1.CreateNamespacedServiceAsync(
                    KubernetesManifestFactory.Service(namespaceName, name, port.Value, exposure),
                    namespaceName,
                    cancellationToken: cancellationToken);
                await CreateExposurePolicyIfNeededAsync(namespaceName, name, port.Value, exposure, cancellationToken);
                var entry = await CreateIngressIfNeededAsync(namespaceName, name, name, port.Value, exposure, spec, cancellationToken);
                (publishedPorts, publicHost, entryUrl) = ResolveEntry(port.Value, exposure, service, entry);
            }

            return new ContainerInstance(
                Id: Guid.NewGuid(),
                CompetitionId: ReadGuid(config.Labels, "competitionId") ?? Guid.Empty,
                TeamId: ReadGuid(config.Labels, "teamId"),
                ChallengeId: ReadGuid(config.Labels, "challengeId"),
                ProviderType: "kubernetes",
                ContainerId: namespaceName,
                PortMappings: publishedPorts,
                Status: "running",
                StartedAt: DateTime.UtcNow,
                ExpectedStopAt: config.Ttl.HasValue ? DateTime.UtcNow.Add(config.Ttl.Value) : null,
                PublicHost: publicHost,
                EntryUrl: entryUrl,
                OrchestrationNamespace: namespaceName,
                InternalHost: port is > 0 ? $"{name}.{namespaceName}.svc.cluster.local" : null,
                InternalPortMappings: port is > 0
                    ? new Dictionary<int, int> { [port.Value] = port.Value }
                    : null);
        }
        catch
        {
            await DeleteNamespaceBestEffortAsync(namespaceName);
            throw;
        }
    }

    public async Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
    {
        var namespaceName = container.OrchestrationNamespace ?? container.ContainerId;
        try
        {
            await EnsureManagedNamespaceAsync(namespaceName, cancellationToken);
            await DeleteNamespaceAsync(namespaceName, cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    public async Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var spec = OrchestrationSpecSerializer.Read(config.OrchestrationJson);
        spec.Image = string.IsNullOrWhiteSpace(spec.Image) ? config.Image : spec.Image;
        spec.Command = string.IsNullOrWhiteSpace(spec.Command) ? config.Command : spec.Command;
        config = ApplyOrchestrationConfig(config, spec);

        var name = KubernetesNames.SafeName($"job-{Guid.NewGuid():N}");
        var ownsNamespace = string.IsNullOrWhiteSpace(config.NetworkName);
        var namespaceName = ownsNamespace
            ? KubernetesNames.InstanceNamespace(_options.NamespacePrefix, name)
            : config.NetworkName!;
        var started = DateTime.UtcNow;

        try
        {
            if (ownsNamespace)
                await PrepareNamespaceAsync(namespaceName, spec, config.Labels, cancellationToken);
            else
                await EnsureManagedNamespaceAsync(namespaceName, cancellationToken);

            var job = KubernetesManifestFactory.Job(namespaceName, name, config, _options, spec);
            await _client.BatchV1.CreateNamespacedJobAsync(job, namespaceName, cancellationToken: cancellationToken);

            var deadline = DateTime.UtcNow.Add(config.Ttl ?? TimeSpan.FromMinutes(5));
            V1Pod? pod = null;
            int exitCode = -1;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pods = await _client.CoreV1.ListNamespacedPodAsync(
                    namespaceName,
                    labelSelector: $"job-name={name}",
                    cancellationToken: cancellationToken);
                pod = pods.Items.FirstOrDefault();
                var terminated = pod?.Status?.ContainerStatuses?.FirstOrDefault()?.State?.Terminated;
                if (terminated is not null)
                {
                    exitCode = terminated.ExitCode;
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            var stdout = pod is null
                ? null
                : await ReadPodLogBestEffortAsync(pod.Metadata.Name, namespaceName, cancellationToken);
            return new ContainerRunResult(
                ContainerId: name,
                ExitCode: exitCode,
                StdOut: stdout,
                StdErr: exitCode == 0 ? null : stdout,
                StartedAt: started,
                FinishedAt: DateTime.UtcNow);
        }
        finally
        {
            if (ownsNamespace)
                await DeleteNamespaceBestEffortAsync(namespaceName);
            else
                await DeleteJobBestEffortAsync(namespaceName, name);
        }
    }

    public async Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default)
    {
        var baseSpec = OrchestrationSpecSerializer.Read(config.OrchestrationJson);
        var services = KubernetesComposeParser.Parse(config.ComposeYaml, baseSpec);
        var namespaceName = KubernetesNames.InstanceNamespace(_options.NamespacePrefix, config.ProjectName);

        try
        {
            await PrepareNamespaceAsync(namespaceName, baseSpec, config.Labels, cancellationToken);

            foreach (var service in services)
            {
                var name = KubernetesNames.SafeName(service.Name);
                var serviceConfig = new ContainerConfig(
                    Image: service.Image,
                    Command: service.Command,
                    EnvironmentVariables: service.Environment,
                    Labels: Merge(config.Labels, service.Labels, new Dictionary<string, string>
                    {
                        [KubernetesManifestFactory.ProjectLabel] = config.ProjectName,
                        [KubernetesManifestFactory.ServiceLabel] = service.Name
                    }),
                    PortMappings: service.Ports.Count > 0 ? service.Ports.ToDictionary(port => port, _ => 0) : null,
                    Entrypoint: service.Entrypoint.Count > 0 ? service.Entrypoint : null,
                    OrchestrationJson: OrchestrationSpecSerializer.Write(service.Orchestration),
                    Ttl: config.Ttl);

                await CreateVolumeDataAsync(namespaceName, service.Orchestration, cancellationToken);

                var labels = new Dictionary<string, string>
                {
                    [KubernetesManifestFactory.ProjectLabel] = config.ProjectName,
                    [KubernetesManifestFactory.ServiceLabel] = service.Name
                };
                var deployment = KubernetesManifestFactory.Deployment(namespaceName, name, serviceConfig, _options, service.Orchestration, labels);
                await _client.AppsV1.CreateNamespacedDeploymentAsync(deployment, namespaceName, cancellationToken: cancellationToken);

                var exposure = KubernetesManifestFactory.ResolveExposure(service.Orchestration, _options);
                if (service.Ports.Count > 0)
                {
                    await _client.CoreV1.CreateNamespacedServiceAsync(
                        KubernetesManifestFactory.Service(namespaceName, name, service.Ports, exposure, labels),
                        namespaceName,
                        cancellationToken: cancellationToken);
                    foreach (var port in service.Ports)
                    {
                        await CreateExposurePolicyIfNeededAsync(namespaceName, name, port, exposure, cancellationToken);
                        var ingressName = service.Ports.Count == 1
                            ? name
                            : KubernetesNames.SafeName($"{name}-{port}");
                        await CreateIngressIfNeededAsync(namespaceName, ingressName, name, port, exposure, service.Orchestration, cancellationToken);
                    }
                }
            }

            await WaitForComposePodsAsync(namespaceName, config.ProjectName, services.Count, cancellationToken);

            return new ComposeDeployment(
                Id: Guid.NewGuid(),
                CompetitionId: ReadGuid(config.Labels, "competitionId") ?? Guid.Empty,
                TeamId: ReadGuid(config.Labels, "teamId"),
                ChallengeId: ReadGuid(config.Labels, "challengeId"),
                ProviderType: "kubernetes",
                ProjectName: config.ProjectName,
                ComposeYaml: config.ComposeYaml,
                Status: "running",
                StartedAt: DateTime.UtcNow,
                ExpectedStopAt: config.Ttl.HasValue ? DateTime.UtcNow.Add(config.Ttl.Value) : null,
                OrchestrationNamespace: namespaceName);
        }
        catch
        {
            await DeleteNamespaceBestEffortAsync(namespaceName);
            throw;
        }
    }

    public async Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default)
    {
        var namespaceName = deployment.OrchestrationNamespace ?? KubernetesNames.InstanceNamespace(_options.NamespacePrefix, deployment.ProjectName);
        await EnsureManagedNamespaceAsync(namespaceName, cancellationToken);
        await DeleteNamespaceAsync(namespaceName, cancellationToken);
    }

    public async Task<ComposeStatus> GetComposeStatusAsync(
        string projectName,
        Dictionary<string, string>? labels = null,
        CancellationToken cancellationToken = default)
    {
        var namespaceName = KubernetesNames.InstanceNamespace(_options.NamespacePrefix, projectName);
        try
        {
            await _client.CoreV1.ReadNamespaceAsync(namespaceName, cancellationToken: cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ComposeStatus(projectName, "not_found", []);
        }

        var pods = await _client.CoreV1.ListNamespacedPodAsync(namespaceName, cancellationToken: cancellationToken);
        var services = await _client.CoreV1.ListNamespacedServiceAsync(namespaceName, cancellationToken: cancellationToken);
        V1IngressList? ingresses = null;
        try
        {
            ingresses = await _client.NetworkingV1.ListNamespacedIngressAsync(namespaceName, cancellationToken: cancellationToken);
        }
        catch
        {
            // Clusters without ingress support still report service status.
        }

        var result = new List<ComposeServiceInstance>();
        foreach (var pod in pods.Items.Where(p => p.Metadata.Labels?.ContainsKey(KubernetesManifestFactory.ServiceLabel) == true))
        {
            var podLabels = pod.Metadata.Labels;
            if (!MatchesLabels(podLabels, labels))
                continue;
            var serviceName = podLabels[KubernetesManifestFactory.ServiceLabel];
            podLabels.TryGetValue("nodeId", out var nodeIdValue);
            var service = services.Items.FirstOrDefault(s => TryGetLabel(s.Metadata.Labels, KubernetesManifestFactory.ServiceLabel) == serviceName);
            var ingress = ingresses?.Items.FirstOrDefault(i => TryGetLabel(i.Metadata.Labels, KubernetesManifestFactory.ServiceLabel) == serviceName);
            var exposure = OrchestrationExposureType.ClusterIP;
            if (service?.Spec?.Type?.Equals("NodePort", StringComparison.OrdinalIgnoreCase) == true)
                exposure = OrchestrationExposureType.NodePort;
            else if (ingress is not null)
                exposure = OrchestrationExposureType.Ingress;
            var ports = ReadServicePorts(service);
            var publicHost = ResolvePublicHost(exposure, ingress);
            var entryUrl = BuildEntryUrl(publicHost, exposure == OrchestrationExposureType.Ingress ? null : ports.Values.FirstOrDefault(), ingress);

            result.Add(new ComposeServiceInstance(
                ServiceName: serviceName,
                ContainerId: pod.Metadata.Name,
                Status: pod.Status?.Phase?.ToLowerInvariant() ?? "unknown",
                NodeId: Guid.TryParse(nodeIdValue, out var nodeId) ? nodeId : null,
                PublishedPorts: ports,
                PublicHost: publicHost,
                EntryUrl: entryUrl,
                InternalHost: service is null ? null : $"{serviceName}.{namespaceName}.svc.cluster.local",
                InternalPortMappings: service is null
                    ? null
                    : service.Spec.Ports
                        .Where(p => p.Port > 0)
                        .ToDictionary(
                            p => int.TryParse(p.TargetPort?.Value, out var target) && target > 0 ? target : p.Port,
                            p => p.Port)));
        }

        var status = result.Count == 0
            ? "not_found"
            : result.Any(s => string.Equals(s.Status, "running", StringComparison.OrdinalIgnoreCase))
                ? "running"
                : "stopped";
        return new ComposeStatus(projectName, status, result);
    }

    private async Task PrepareNamespaceAsync(
        string namespaceName,
        OrchestrationSpec spec,
        Dictionary<string, string>? labels,
        CancellationToken ct)
    {
        await _client.CoreV1.CreateNamespaceAsync(
            KubernetesManifestFactory.Namespace(namespaceName, labels),
            cancellationToken: ct);
        foreach (var registry in _options.Registries.Where(r =>
                     !string.IsNullOrWhiteSpace(r.Registry) &&
                     !string.IsNullOrWhiteSpace(r.UserName) &&
                     !string.IsNullOrWhiteSpace(r.Password)))
        {
            await _client.CoreV1.CreateNamespacedSecretAsync(
                KubernetesRegistrySecretFactory.Build(namespaceName, registry),
                namespaceName,
                cancellationToken: ct);
        }
        await _client.CoreV1.CreateNamespacedResourceQuotaAsync(
            KubernetesManifestFactory.ResourceQuota(namespaceName, _options),
            namespaceName,
            cancellationToken: ct);
        await _client.CoreV1.CreateNamespacedLimitRangeAsync(
            KubernetesManifestFactory.LimitRange(namespaceName, _options),
            namespaceName,
            cancellationToken: ct);
        foreach (var policy in KubernetesManifestFactory.NetworkPolicies(
                     namespaceName,
                     KubernetesManifestFactory.ResolveNetworkMode(spec, _options)))
        {
            await _client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(policy, namespaceName, cancellationToken: ct);
        }
        await CreateVolumeDataAsync(namespaceName, spec, ct);
    }

    private async Task CreateVolumeDataAsync(string namespaceName, OrchestrationSpec spec, CancellationToken ct)
    {
        foreach (var volume in spec.Kubernetes.Volumes.Where(v => v.Data.Count > 0))
        {
            try
            {
                if (volume.Type.Equals("configMap", StringComparison.OrdinalIgnoreCase))
                {
                    await _client.CoreV1.CreateNamespacedConfigMapAsync(
                        KubernetesManifestFactory.ConfigMapVolume(namespaceName, volume),
                        namespaceName,
                        cancellationToken: ct);
                }
                else if (volume.Type.Equals("secret", StringComparison.OrdinalIgnoreCase))
                {
                    await _client.CoreV1.CreateNamespacedSecretAsync(
                        KubernetesManifestFactory.SecretVolume(namespaceName, volume),
                        namespaceName,
                        cancellationToken: ct);
                }
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
            {
            }
        }
    }

    private async Task<V1Ingress?> CreateIngressIfNeededAsync(
        string namespaceName,
        string name,
        string serviceName,
        int port,
        OrchestrationExposureType exposure,
        OrchestrationSpec spec,
        CancellationToken ct)
    {
        if (exposure != OrchestrationExposureType.Ingress)
            return null;

        var ingressSpec = spec.Kubernetes.Ingress;
        ingressSpec.BaseDomain ??= _options.IngressBaseDomain;
        ingressSpec.ClassName ??= _options.IngressClassName;
        ingressSpec.TlsSecretName ??= _options.IngressTlsSecretName;
        var host = ResolveIngressHost(name, ingressSpec);
        return await _client.NetworkingV1.CreateNamespacedIngressAsync(
            KubernetesManifestFactory.Ingress(namespaceName, name, host, serviceName, port, ingressSpec, new Dictionary<string, string>
            {
                [KubernetesManifestFactory.ServiceLabel] = serviceName
            }),
            namespaceName,
            cancellationToken: ct);
    }

    private async Task CreateExposurePolicyIfNeededAsync(
        string namespaceName,
        string name,
        int port,
        OrchestrationExposureType exposure,
        CancellationToken ct)
    {
        if (exposure is not (OrchestrationExposureType.NodePort or OrchestrationExposureType.Ingress))
            return;

        try
        {
            await _client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
                KubernetesManifestFactory.ExposedIngressPolicy(namespaceName, name, port),
                namespaceName,
                cancellationToken: ct);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
        {
        }
    }

    private (Dictionary<int, int> Ports, string? Host, string? Url) ResolveEntry(
        int containerPort,
        OrchestrationExposureType exposure,
        V1Service service,
        V1Ingress? ingress)
    {
        var ports = ReadServicePorts(service);
        if (exposure == OrchestrationExposureType.Ingress)
        {
            var host = ResolvePublicHost(exposure, ingress);
            return (ports.Count > 0 ? ports : new Dictionary<int, int> { [containerPort] = containerPort }, host, BuildEntryUrl(host, null, ingress));
        }
        return (ports, ResolvePublicHost(exposure, ingress), null);
    }

    private static Dictionary<int, int> ReadServicePorts(V1Service? service)
    {
        var result = new Dictionary<int, int>();
        if (service?.Spec?.Ports is null) return result;
        foreach (var port in service.Spec.Ports)
        {
            var target = int.TryParse(port.TargetPort?.Value, out var intValue) ? intValue : port.Port;
            var published = service.Spec.Type?.Equals("NodePort", StringComparison.OrdinalIgnoreCase) == true
                ? port.NodePort.GetValueOrDefault()
                : port.Port;
            if (target > 0 && published > 0)
                result[target] = published;
        }
        return result;
    }

    private string? ResolvePublicHost(OrchestrationExposureType exposure, V1Ingress? ingress)
        => exposure switch
        {
            OrchestrationExposureType.Ingress => ingress?.Spec?.Rules?.FirstOrDefault()?.Host,
            OrchestrationExposureType.NodePort => string.IsNullOrWhiteSpace(_options.PublicEntry) ? null : _options.PublicEntry,
            OrchestrationExposureType.ClusterIP => null,
            _ => null
        };

    private static string? BuildEntryUrl(string? host, int? port, V1Ingress? ingress)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        var scheme = ingress?.Spec?.Tls?.Count > 0 ? "https" : "http";
        if (port is > 0)
            return $"{host}:{port}";
        return $"{scheme}://{host}";
    }

    private static string ResolveIngressHost(string name, KubernetesIngressSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.Host))
            return spec.Host;
        if (string.IsNullOrWhiteSpace(spec.BaseDomain))
            throw new InvalidOperationException("K8s ingress exposure requires K8s:IngressBaseDomain or orchestration.kubernetes.ingress.baseDomain.");
        return $"{name}.{spec.BaseDomain.Trim('.')}";
    }

    private async Task WaitForComposePodsAsync(
        string namespaceName,
        string projectName,
        int expectedPods,
        CancellationToken ct)
    {
        if (expectedPods <= 0)
            return;

        var deadline = DateTime.UtcNow.AddSeconds(30);
        var projectLabel = KubernetesNames.SafeName(projectName);
        while (DateTime.UtcNow < deadline)
        {
            var pods = await _client.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                labelSelector: $"{KubernetesManifestFactory.ProjectLabel}={projectLabel}",
                cancellationToken: ct);
            if (pods.Items.Count >= expectedPods)
                return;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task<string?> ReadPodLogBestEffortAsync(string podName, string namespaceName, CancellationToken ct)
    {
        try
        {
            await using var stream = await _client.CoreV1.ReadNamespacedPodLogAsync(podName, namespaceName, cancellationToken: ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read Kubernetes pod log for {Namespace}/{Pod}.", namespaceName, podName);
            return null;
        }
    }

    private async Task EnsureManagedNamespaceAsync(string namespaceName, CancellationToken ct)
    {
        var expectedPrefix = KubernetesNames.SafeName(_options.NamespacePrefix);
        if (string.IsNullOrWhiteSpace(namespaceName) ||
            !namespaceName.StartsWith($"{expectedPrefix}-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kubernetes runner refused to delete an unmanaged namespace.");
        }

        var ns = await _client.CoreV1.ReadNamespaceAsync(namespaceName, cancellationToken: ct);
        if (ns.Metadata?.Labels is null ||
            !ns.Metadata.Labels.TryGetValue(KubernetesManifestFactory.ManagedByLabel, out var managedBy) ||
            !managedBy.Equals("noctf-runner", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kubernetes runner refused to delete a namespace without its managed label.");
        }
    }

    private async Task DeleteNamespaceBestEffortAsync(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
            return;
        try
        {
            await DeleteNamespaceAsync(namespaceName, CancellationToken.None);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete Kubernetes namespace {Namespace}.", namespaceName);
        }
    }

    private async Task DeleteNamespaceAsync(string namespaceName, CancellationToken cancellationToken)
    {
        using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanupCts.CancelAfter(TimeSpan.FromSeconds(30));
        await _client.CoreV1.DeleteNamespaceAsync(namespaceName, cancellationToken: cleanupCts.Token);
    }

    private async Task DeleteJobBestEffortAsync(string namespaceName, string jobName)
    {
        try
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _client.BatchV1.DeleteNamespacedJobAsync(
                jobName,
                namespaceName,
                body: new V1DeleteOptions { PropagationPolicy = "Background" },
                cancellationToken: cleanupCts.Token);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete Kubernetes job {Namespace}/{Job}.", namespaceName, jobName);
        }
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, string>? labels, string key)
        => labels is not null && labels.TryGetValue(key, out var value) && Guid.TryParse(value, out var guid)
            ? guid
            : null;

    private static ContainerConfig ApplyOrchestrationConfig(ContainerConfig config, OrchestrationSpec spec)
        => config with
        {
            Image = string.IsNullOrWhiteSpace(spec.Image) ? config.Image : spec.Image,
            Command = string.IsNullOrWhiteSpace(spec.Command) ? config.Command : spec.Command,
            Entrypoint = spec.Entrypoint.Count > 0 ? spec.Entrypoint : config.Entrypoint
        };

    private static bool MatchesLabels(IDictionary<string, string>? source, IReadOnlyDictionary<string, string>? expected)
    {
        if (expected is null || expected.Count == 0)
            return true;
        if (source is null)
            return false;
        return expected.All(kvp =>
            source.TryGetValue(kvp.Key, out var value) &&
            KubernetesNames.SafeName(kvp.Value).Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> Merge(params IReadOnlyDictionary<string, string>?[] maps)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var map in maps)
        {
            foreach (var (key, value) in map ?? new Dictionary<string, string>())
                result[key] = value;
        }
        return result;
    }

    private static string? TryGetLabel(IDictionary<string, string>? labels, string key)
        => labels is not null && labels.TryGetValue(key, out var value) ? value : null;
}
