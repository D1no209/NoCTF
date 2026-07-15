using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        var fingerprint = ComputeContainerFingerprint(config);
        config = config with
        {
            Labels = WithOperationMetadata(config.Labels, config.OperationId, fingerprint)
        };
        AddRuntimeExpiryMetadata(config.Labels, config.OperationId, config.Ttl);
        var spec = OrchestrationSpecSerializer.Read(config.OrchestrationJson);
        spec.Image = string.IsNullOrWhiteSpace(spec.Image) ? config.Image : spec.Image;
        spec.Command = string.IsNullOrWhiteSpace(spec.Command) ? config.Command : spec.Command;
        spec.ExposedPort ??= config.PortMappings?.Keys.FirstOrDefault(port => port > 0);
        config = ApplyOrchestrationConfig(config, spec);

        var runtimeIdentity = config.OperationId?.ToString("N") ?? Guid.NewGuid().ToString("N");
        var nameSeed = config.NetworkAliases?.FirstOrDefault(alias => !string.IsNullOrWhiteSpace(alias))
            ?? $"ctf-{Path.GetFileNameWithoutExtension(spec.Image).Split(':')[0]}-{runtimeIdentity[..8]}";
        var name = KubernetesNames.SafeName(nameSeed);
        var namespaceSeed = config.OperationId.HasValue
            ? $"container-{runtimeIdentity}"
            : $"{name}-{runtimeIdentity}";
        var namespaceName = KubernetesNames.InstanceNamespace(_options.NamespacePrefix, namespaceSeed);
        var namespaceCreated = false;
        try
        {
            namespaceCreated = await EnsureOperationNamespaceAsync(
                namespaceName,
                config.Labels,
                config.OperationId,
                fingerprint,
                cancellationToken);
            await EnsureNamespaceResourcesAsync(namespaceName, spec, cancellationToken);

            var deployment = KubernetesManifestFactory.Deployment(namespaceName, name, config, _options, spec);
            var persistedDeployment = await EnsureDeploymentAsync(deployment, namespaceName, cancellationToken);

            var port = KubernetesManifestFactory.ResolvePort(config, spec);
            Dictionary<int, int> publishedPorts = [];
            string? publicHost = null;
            string? entryUrl = null;
            if (port is > 0)
            {
                var exposure = KubernetesManifestFactory.ResolveExposure(spec, _options);
                var service = await EnsureServiceAsync(
                    KubernetesManifestFactory.Service(
                        namespaceName,
                        name,
                        port.Value,
                        exposure,
                        config.Labels),
                    namespaceName,
                    cancellationToken);
                await CreateExposurePolicyIfNeededAsync(namespaceName, name, port.Value, exposure, cancellationToken);
                var entry = await CreateIngressIfNeededAsync(
                    namespaceName,
                    name,
                    name,
                    port.Value,
                    exposure,
                    spec,
                    config.Labels,
                    cancellationToken);
                (publishedPorts, publicHost, entryUrl) = ResolveEntry(port.Value, exposure, service, entry);
            }

            await WaitForPodsReadyAsync(
                namespaceName,
                $"app={name}",
                expectedPods: 1,
                timeout: ReadinessTimeout(),
                cancellationToken);

            var startedAt = persistedDeployment.Metadata?.CreationTimestamp?.ToUniversalTime()
                ?? DateTime.UtcNow;

            return new ContainerInstance(
                Id: Guid.NewGuid(),
                CompetitionId: ReadGuid(config.Labels, "competitionId") ?? Guid.Empty,
                TeamId: ReadGuid(config.Labels, "teamId"),
                ChallengeId: ReadGuid(config.Labels, "challengeId"),
                ProviderType: "kubernetes",
                ContainerId: namespaceName,
                PortMappings: publishedPorts,
                Status: "running",
                StartedAt: startedAt,
                ExpectedStopAt: config.Ttl.HasValue ? startedAt.Add(config.Ttl.Value) : null,
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
            if (namespaceCreated && !config.OperationId.HasValue)
                await DeleteNamespaceBestEffortAsync(namespaceName);
            throw;
        }
    }

    public async Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
    {
        var namespaceName = container.OrchestrationNamespace ?? container.ContainerId;
        try
        {
            var existing = await EnsureManagedNamespaceAsync(namespaceName, cancellationToken);
            EnsureNamespaceMatchesRuntime(
                existing,
                container.CompetitionId,
                container.TeamId,
                container.ChallengeId,
                projectName: null);
            var namespaceUid = existing.Metadata?.Uid;
            await DeleteNamespaceAsync(namespaceName, cancellationToken, namespaceUid);
            await WaitForNamespaceDeletedAsync(namespaceName, namespaceUid, ReadinessTimeout(), cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    public async Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var fingerprint = ComputeContainerFingerprint(config);
        var runTimeout = NormalizeRunTimeout(config.Ttl);
        config = config with
        {
            Labels = WithOperationMetadata(config.Labels, config.OperationId, fingerprint)
        };
        if (config.OperationId.HasValue)
        {
            var receiptExpires = DateTimeOffset.UtcNow
                .Add(runTimeout)
                .AddSeconds(_options.RunReceiptRetentionSeconds)
                .ToUnixTimeSeconds();
            config.Labels![KubernetesManifestFactory.RunReceiptLabel] = bool.TrueString;
            config.Labels[KubernetesManifestFactory.RunReceiptExpiresLabel] =
                receiptExpires.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var spec = OrchestrationSpecSerializer.Read(config.OrchestrationJson);
        spec.Image = string.IsNullOrWhiteSpace(spec.Image) ? config.Image : spec.Image;
        spec.Command = string.IsNullOrWhiteSpace(spec.Command) ? config.Command : spec.Command;
        config = ApplyOrchestrationConfig(config, spec);

        var runtimeIdentity = config.OperationId?.ToString("N") ?? Guid.NewGuid().ToString("N");
        var name = KubernetesNames.SafeName($"job-{runtimeIdentity}");
        var ownsNamespace = string.IsNullOrWhiteSpace(config.NetworkName);
        var receiptNamespaceName = config.OperationId.HasValue
            ? KubernetesNames.InstanceNamespace(_options.NamespacePrefix, $"run-{runtimeIdentity}")
            : null;
        var namespaceName = ownsNamespace
            ? receiptNamespaceName ?? KubernetesNames.InstanceNamespace(_options.NamespacePrefix, name)
            : config.NetworkName!;
        var canCleanupOwnedNamespace = false;

        try
        {
            if (ownsNamespace)
            {
                await EnsureOperationNamespaceAsync(
                    namespaceName,
                    config.Labels,
                    config.OperationId,
                    fingerprint,
                    cancellationToken);
                canCleanupOwnedNamespace = true;
                await EnsureNamespaceResourcesAsync(namespaceName, spec, cancellationToken);
            }
            else
            {
                await EnsureManagedNamespaceAsync(namespaceName, cancellationToken);
                if (receiptNamespaceName is not null)
                {
                    await EnsureOperationNamespaceAsync(
                        receiptNamespaceName,
                        config.Labels,
                        config.OperationId,
                        fingerprint,
                        cancellationToken);
                }
            }

            var job = await TryGetExistingJobAsync(
                namespaceName,
                name,
                config.OperationId,
                fingerprint,
                cancellationToken);
            if (job is null)
            {
                var manifest = KubernetesManifestFactory.Job(namespaceName, name, config, _options, spec);
                manifest.Spec.ActiveDeadlineSeconds = Math.Max(1L, (long)Math.Ceiling(runTimeout.TotalSeconds));
                manifest.Spec.TtlSecondsAfterFinished = (int)Math.Clamp(
                    (long)Math.Ceiling(runTimeout.TotalSeconds) + _options.RunReceiptRetentionSeconds,
                    60,
                    172_800);
                try
                {
                    job = await _client.BatchV1.CreateNamespacedJobAsync(
                        manifest,
                        namespaceName,
                        cancellationToken: cancellationToken);
                }
                catch (HttpOperationException ex) when (
                    ex.Response.StatusCode == HttpStatusCode.Conflict &&
                    config.OperationId.HasValue)
                {
                    job = await TryGetExistingJobAsync(
                        namespaceName,
                        name,
                        config.OperationId,
                        fingerprint,
                        cancellationToken);
                    if (job is null)
                        throw;
                }
            }

            var started = job.Metadata?.CreationTimestamp?.ToUniversalTime() ?? DateTime.UtcNow;
            var deadline = started.Add(runTimeout);
            V1Pod? pod = null;
            var exitCode = 124;
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

                var delay = deadline - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(
                        delay < TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1),
                        cancellationToken);
            }

            var stdout = pod is null
                ? null
                : await ReadPodLogBestEffortAsync(pod.Metadata.Name, namespaceName, cancellationToken);
            var finishedAt = DateTime.UtcNow;
            return new ContainerRunResult(
                ContainerId: name,
                ExitCode: exitCode,
                StdOut: stdout,
                StdErr: exitCode == 0 ? null : stdout,
                StartedAt: started,
                FinishedAt: finishedAt);
        }
        finally
        {
            var retainReceipt = config.OperationId.HasValue;
            if (!retainReceipt && ownsNamespace && canCleanupOwnedNamespace)
                await DeleteNamespaceBestEffortAsync(namespaceName);
            else if (!retainReceipt && !ownsNamespace)
                await DeleteJobBestEffortAsync(namespaceName, name);
        }
    }

    public async Task<int> CleanupExpiredRunReceiptsAsync(CancellationToken ct = default)
    {
        var namespaces = await _client.CoreV1.ListNamespaceAsync(
            labelSelector: $"{KubernetesManifestFactory.RunReceiptLabel}={KubernetesNames.SafeName(bool.TrueString)}",
            cancellationToken: ct);
        var expectedPrefix = $"{KubernetesNames.InstancePrefix(_options.NamespacePrefix)}-";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var deleted = 0;
        foreach (var candidate in namespaces.Items)
        {
            var metadata = candidate.Metadata;
            var labels = metadata?.Labels;
            if (metadata is null ||
                string.IsNullOrWhiteSpace(metadata.Name) ||
                !metadata.Name.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
                metadata.DeletionTimestamp is not null ||
                labels is null ||
                !labels.TryGetValue(KubernetesManifestFactory.ManagedByLabel, out var managedBy) ||
                !string.Equals(managedBy, "noctf-runner", StringComparison.Ordinal) ||
                !labels.TryGetValue(KubernetesManifestFactory.OperationLabel, out var operation) ||
                !Guid.TryParse(operation, out _) ||
                !labels.ContainsKey(KubernetesManifestFactory.SpecFingerprintLabel) ||
                !labels.TryGetValue(KubernetesManifestFactory.RunReceiptExpiresLabel, out var expiresText) ||
                !long.TryParse(
                    expiresText,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var expiresAt) ||
                expiresAt > now)
            {
                continue;
            }

            try
            {
                await DeleteNamespaceAsync(metadata.Name, ct, metadata.Uid);
                deleted++;
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        return deleted;
    }

    public async Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default)
    {
        var fingerprint = ComputeComposeFingerprint(config);
        config = config with
        {
            Labels = WithOperationMetadata(config.Labels, config.OperationId, fingerprint)
        };
        config.Labels![KubernetesManifestFactory.ProjectLabel] = config.ProjectName;
        AddRuntimeExpiryMetadata(config.Labels, config.OperationId, config.Ttl);
        var baseSpec = OrchestrationSpecSerializer.Read(config.OrchestrationJson);
        var services = KubernetesComposeParser.Parse(config.ComposeYaml, baseSpec);
        var normalizedServiceNames = services
            .GroupBy(service => KubernetesNames.SafeName(service.Name), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(service => service.Name)))
            .ToArray();
        if (normalizedServiceNames.Length > 0)
        {
            throw new InvalidOperationException(
                $"Compose service names collide after Kubernetes normalization: {string.Join("; ", normalizedServiceNames)}.");
        }
        var namespaceSeed = config.OperationId.HasValue
            ? $"compose-{config.OperationId.Value:N}"
            : config.ProjectName;
        var namespaceName = KubernetesNames.InstanceNamespace(_options.NamespacePrefix, namespaceSeed);
        var namespaceCreated = false;
        try
        {
            namespaceCreated = await EnsureOperationNamespaceAsync(
                namespaceName,
                config.Labels,
                config.OperationId,
                fingerprint,
                cancellationToken);
            await EnsureNamespaceResourcesAsync(namespaceName, baseSpec, cancellationToken);

            DateTime? startedAt = null;
            foreach (var service in services)
            {
                var name = KubernetesNames.SafeName(service.Name);
                var labels = Merge(config.Labels, new Dictionary<string, string>
                {
                    [KubernetesManifestFactory.ProjectLabel] = config.ProjectName,
                    [KubernetesManifestFactory.ServiceLabel] = service.Name
                });
                var serviceConfig = new ContainerConfig(
                    Image: service.Image,
                    Command: service.Command,
                    EnvironmentVariables: service.Environment,
                    Labels: Merge(labels, service.Labels),
                    PortMappings: service.Ports.Count > 0 ? service.Ports.ToDictionary(port => port, _ => 0) : null,
                    Entrypoint: service.Entrypoint.Count > 0 ? service.Entrypoint : null,
                    OrchestrationJson: OrchestrationSpecSerializer.Write(service.Orchestration),
                    Ttl: config.Ttl,
                    OperationId: config.OperationId);

                await CreateVolumeDataAsync(namespaceName, service.Orchestration, cancellationToken);

                var deployment = KubernetesManifestFactory.Deployment(namespaceName, name, serviceConfig, _options, service.Orchestration, labels);
                var persistedDeployment = await EnsureDeploymentAsync(deployment, namespaceName, cancellationToken);
                if (persistedDeployment.Metadata?.CreationTimestamp is { } createdAt)
                {
                    var serviceStartedAt = createdAt.ToUniversalTime();
                    startedAt = !startedAt.HasValue || serviceStartedAt < startedAt.Value
                        ? serviceStartedAt
                        : startedAt;
                }

                var exposure = KubernetesManifestFactory.ResolveExposure(service.Orchestration, _options);
                if (service.Ports.Count > 0)
                {
                    await EnsureServiceAsync(
                        KubernetesManifestFactory.Service(namespaceName, name, service.Ports, exposure, labels),
                        namespaceName,
                        cancellationToken);
                    foreach (var port in service.Ports)
                    {
                        await CreateExposurePolicyIfNeededAsync(namespaceName, name, port, exposure, cancellationToken);
                        var ingressName = service.Ports.Count == 1
                            ? name
                            : KubernetesNames.SafeName($"{name}-{port}");
                        await CreateIngressIfNeededAsync(
                            namespaceName,
                            ingressName,
                            name,
                            port,
                            exposure,
                            service.Orchestration,
                            labels,
                            cancellationToken);
                    }
                }
            }

            await WaitForComposePodsAsync(namespaceName, config.ProjectName, services.Count, cancellationToken);
            startedAt ??= DateTime.UtcNow;

            return new ComposeDeployment(
                Id: Guid.NewGuid(),
                CompetitionId: ReadGuid(config.Labels, "competitionId") ?? Guid.Empty,
                TeamId: ReadGuid(config.Labels, "teamId"),
                ChallengeId: ReadGuid(config.Labels, "challengeId"),
                ProviderType: "kubernetes",
                ProjectName: config.ProjectName,
                ComposeYaml: config.ComposeYaml,
                Status: "running",
                StartedAt: startedAt.Value,
                ExpectedStopAt: config.Ttl.HasValue ? startedAt.Value.Add(config.Ttl.Value) : null,
                OrchestrationNamespace: namespaceName);
        }
        catch
        {
            if (namespaceCreated && !config.OperationId.HasValue)
                await DeleteNamespaceBestEffortAsync(namespaceName);
            throw;
        }
    }

    public async Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default)
    {
        var namespaceName = deployment.OrchestrationNamespace ?? KubernetesNames.InstanceNamespace(_options.NamespacePrefix, deployment.ProjectName);
        try
        {
            var existing = await EnsureManagedNamespaceAsync(namespaceName, cancellationToken);
            EnsureNamespaceMatchesRuntime(
                existing,
                deployment.CompetitionId,
                deployment.TeamId,
                deployment.ChallengeId,
                deployment.ProjectName);
            var namespaceUid = existing.Metadata?.Uid;
            await DeleteNamespaceAsync(namespaceName, cancellationToken, namespaceUid);
            await WaitForNamespaceDeletedAsync(namespaceName, namespaceUid, ReadinessTimeout(), cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
        }
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
            var projectValue = KubernetesNames.SafeName(projectName);
            var namespaces = await _client.CoreV1.ListNamespaceAsync(
                labelSelector: $"{KubernetesManifestFactory.ProjectLabel}={projectValue}",
                cancellationToken: cancellationToken);
            var candidate = namespaces.Items
                .Where(ns => ns.Metadata?.Labels is not null &&
                             ns.Metadata.Name.StartsWith(
                                 $"{KubernetesNames.InstancePrefix(_options.NamespacePrefix)}-",
                                 StringComparison.Ordinal) &&
                             MatchesLabels(ns.Metadata.Labels, labels))
                .OrderByDescending(ns => ns.Metadata.CreationTimestamp)
                .FirstOrDefault();
            if (candidate?.Metadata?.Name is null)
                return new ComposeStatus(projectName, "not_found", []);
            namespaceName = candidate.Metadata.Name;
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

    private Task CreateNamespaceAsync(
        string namespaceName,
        Dictionary<string, string>? labels,
        CancellationToken ct)
        => _client.CoreV1.CreateNamespaceAsync(
            KubernetesManifestFactory.Namespace(namespaceName, labels),
            cancellationToken: ct);

    private async Task<bool> EnsureOperationNamespaceAsync(
        string namespaceName,
        Dictionary<string, string>? labels,
        Guid? operationId,
        string fingerprint,
        CancellationToken ct)
    {
        while (true)
        {
            V1Namespace? existing = null;
            try
            {
                existing = await _client.CoreV1.ReadNamespaceAsync(namespaceName, cancellationToken: ct);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
            }

            if (existing is not null)
            {
                if (existing.Metadata?.DeletionTimestamp is not null)
                {
                    await WaitForNamespaceDeletedAsync(
                        namespaceName,
                        existing.Metadata.Uid,
                        ReadinessTimeout(),
                        ct);
                    continue;
                }

                EnsureNamespaceOperationOwner(existing, namespaceName, operationId, fingerprint);
                return false;
            }

            try
            {
                await CreateNamespaceAsync(namespaceName, labels, ct);
                return true;
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
            {
                // A concurrent creator won publication. Re-read and validate
                // both its operation identity and immutable request fingerprint.
            }
        }
    }

    private async Task EnsureNamespaceResourcesAsync(
        string namespaceName,
        OrchestrationSpec spec,
        CancellationToken ct)
    {
        foreach (var registry in _options.Registries.Where(r =>
                     !string.IsNullOrWhiteSpace(r.Registry) &&
                     !string.IsNullOrWhiteSpace(r.UserName) &&
                     !string.IsNullOrWhiteSpace(r.Password)))
        {
            var secret = KubernetesRegistrySecretFactory.Build(namespaceName, registry);
            await EnsureCreatedAsync(
                secret,
                token => _client.CoreV1.CreateNamespacedSecretAsync(secret, namespaceName, cancellationToken: token),
                token => _client.CoreV1.ReadNamespacedSecretAsync(secret.Metadata.Name, namespaceName, cancellationToken: token),
                ct);
        }
        var quota = KubernetesManifestFactory.ResourceQuota(namespaceName, _options);
        await EnsureCreatedAsync(
            quota,
            token => _client.CoreV1.CreateNamespacedResourceQuotaAsync(quota, namespaceName, cancellationToken: token),
            token => _client.CoreV1.ReadNamespacedResourceQuotaAsync(quota.Metadata.Name, namespaceName, cancellationToken: token),
            ct);
        var limitRange = KubernetesManifestFactory.LimitRange(namespaceName, _options);
        await EnsureCreatedAsync(
            limitRange,
            token => _client.CoreV1.CreateNamespacedLimitRangeAsync(limitRange, namespaceName, cancellationToken: token),
            token => _client.CoreV1.ReadNamespacedLimitRangeAsync(limitRange.Metadata.Name, namespaceName, cancellationToken: token),
            ct);
        foreach (var policy in KubernetesManifestFactory.NetworkPolicies(
                     namespaceName,
                     KubernetesManifestFactory.ResolveNetworkMode(spec, _options)))
        {
            await EnsureCreatedAsync(
                policy,
                token => _client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(policy, namespaceName, cancellationToken: token),
                token => _client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(policy.Metadata.Name, namespaceName, cancellationToken: token),
                ct);
        }
        await CreateVolumeDataAsync(namespaceName, spec, ct);
    }

    private async Task CreateVolumeDataAsync(string namespaceName, OrchestrationSpec spec, CancellationToken ct)
    {
        foreach (var volume in spec.Kubernetes.Volumes.Where(v => v.Data.Count > 0))
        {
            if (volume.Type.Equals("configMap", StringComparison.OrdinalIgnoreCase))
            {
                var configMap = KubernetesManifestFactory.ConfigMapVolume(namespaceName, volume);
                await EnsureCreatedAsync(
                    configMap,
                    token => _client.CoreV1.CreateNamespacedConfigMapAsync(configMap, namespaceName, cancellationToken: token),
                    token => _client.CoreV1.ReadNamespacedConfigMapAsync(configMap.Metadata.Name, namespaceName, cancellationToken: token),
                    ct);
            }
            else if (volume.Type.Equals("secret", StringComparison.OrdinalIgnoreCase))
            {
                var secret = KubernetesManifestFactory.SecretVolume(namespaceName, volume);
                await EnsureCreatedAsync(
                    secret,
                    token => _client.CoreV1.CreateNamespacedSecretAsync(secret, namespaceName, cancellationToken: token),
                    token => _client.CoreV1.ReadNamespacedSecretAsync(secret.Metadata.Name, namespaceName, cancellationToken: token),
                    ct);
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
        IReadOnlyDictionary<string, string>? labels,
        CancellationToken ct)
    {
        if (exposure != OrchestrationExposureType.Ingress)
            return null;

        var ingressSpec = spec.Kubernetes.Ingress;
        ingressSpec.BaseDomain ??= _options.IngressBaseDomain;
        ingressSpec.ClassName ??= _options.IngressClassName;
        ingressSpec.TlsSecretName ??= _options.IngressTlsSecretName;
        var host = ResolveIngressHost(name, ingressSpec);
        var ingress = KubernetesManifestFactory.Ingress(
            namespaceName,
            name,
            host,
            serviceName,
            port,
            ingressSpec,
            Merge(labels, new Dictionary<string, string>
            {
                [KubernetesManifestFactory.ServiceLabel] = serviceName
            }));
        return await EnsureCreatedAsync(
            ingress,
            token => _client.NetworkingV1.CreateNamespacedIngressAsync(ingress, namespaceName, cancellationToken: token),
            token => _client.NetworkingV1.ReadNamespacedIngressAsync(name, namespaceName, cancellationToken: token),
            ct);
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

        var policy = KubernetesManifestFactory.ExposedIngressPolicy(namespaceName, name, port);
        await EnsureCreatedAsync(
            policy,
            token => _client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(policy, namespaceName, cancellationToken: token),
            token => _client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(policy.Metadata.Name, namespaceName, cancellationToken: token),
            ct);
    }

    private Task<V1Deployment> EnsureDeploymentAsync(
        V1Deployment deployment,
        string namespaceName,
        CancellationToken ct)
        => EnsureCreatedAsync(
            deployment,
            token => _client.AppsV1.CreateNamespacedDeploymentAsync(deployment, namespaceName, cancellationToken: token),
            token => _client.AppsV1.ReadNamespacedDeploymentAsync(deployment.Metadata.Name, namespaceName, cancellationToken: token),
            ct);

    private Task<V1Service> EnsureServiceAsync(
        V1Service service,
        string namespaceName,
        CancellationToken ct)
        => EnsureCreatedAsync(
            service,
            token => _client.CoreV1.CreateNamespacedServiceAsync(service, namespaceName, cancellationToken: token),
            token => _client.CoreV1.ReadNamespacedServiceAsync(service.Metadata.Name, namespaceName, cancellationToken: token),
            ct);

    private static async Task<T> EnsureCreatedAsync<T>(
        T desired,
        Func<CancellationToken, Task<T>> create,
        Func<CancellationToken, Task<T>> read,
        CancellationToken ct)
        where T : IKubernetesObject<V1ObjectMeta>
    {
        try
        {
            return await create(ct);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await read(ct);
            EnsureMatchingManagedResource(desired.Metadata, existing.Metadata);
            return existing;
        }
    }

    private static void EnsureMatchingManagedResource(V1ObjectMeta desired, V1ObjectMeta existing)
    {
        if (existing.Labels is null ||
            !existing.Labels.TryGetValue(KubernetesManifestFactory.ManagedByLabel, out var managedBy) ||
            !string.Equals(managedBy, "noctf-runner", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Kubernetes resource '{existing.NamespaceProperty}/{existing.Name}' is not managed by NoCTF.");
        }

        foreach (var (key, expected) in desired.Labels ?? new Dictionary<string, string>())
        {
            if (existing.Labels.TryGetValue(key, out var actual) &&
                string.Equals(actual, expected, StringComparison.Ordinal))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Kubernetes resource '{existing.NamespaceProperty}/{existing.Name}' conflicts with the current Runner operation.");
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

        var projectLabel = KubernetesNames.SafeName(projectName);
        await WaitForPodsReadyAsync(
            namespaceName,
            $"{KubernetesManifestFactory.ProjectLabel}={projectLabel}",
            expectedPods,
            ReadinessTimeout(),
            ct);
    }

    private static void EnsureNamespaceOperationOwner(
        V1Namespace existingNamespace,
        string namespaceName,
        Guid? operationId,
        string fingerprint)
    {
        if (existingNamespace.Metadata?.Labels is null ||
            !existingNamespace.Metadata.Labels.TryGetValue(KubernetesManifestFactory.ManagedByLabel, out var managedBy) ||
            !string.Equals(managedBy, "noctf-runner", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Kubernetes namespace '{namespaceName}' is not managed by NoCTF.");
        }

        if (!operationId.HasValue)
        {
            throw new InvalidOperationException(
                $"Kubernetes namespace '{namespaceName}' already exists and cannot be recovered without an operation id.");
        }

        var expectedOperationId = KubernetesNames.SafeName(operationId.Value.ToString("D"));
        if (!existingNamespace.Metadata.Labels.TryGetValue(KubernetesManifestFactory.OperationLabel, out var actualOperationId) ||
            !string.Equals(actualOperationId, expectedOperationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Kubernetes namespace '{namespaceName}' belongs to another Runner operation.");
        }

        if (!existingNamespace.Metadata.Labels.TryGetValue(KubernetesManifestFactory.SpecFingerprintLabel, out var actualFingerprint) ||
            !string.Equals(actualFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Kubernetes operation id for namespace '{namespaceName}' was reused with a different request.");
        }
    }

    private async Task<V1Job?> TryGetExistingJobAsync(
        string namespaceName,
        string name,
        Guid? operationId,
        string fingerprint,
        CancellationToken ct)
    {
        if (!operationId.HasValue)
            return null;

        try
        {
            var job = await _client.BatchV1.ReadNamespacedJobAsync(
                name,
                namespaceName,
                cancellationToken: ct);
            var expectedOperationId = KubernetesNames.SafeName(operationId.Value.ToString("D"));
            if (job.Metadata?.Labels is null ||
                !job.Metadata.Labels.TryGetValue(KubernetesManifestFactory.OperationLabel, out var actualOperationId) ||
                !string.Equals(actualOperationId, expectedOperationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Kubernetes job '{namespaceName}/{name}' belongs to another Runner operation.");
            }

            if (!job.Metadata.Labels.TryGetValue(KubernetesManifestFactory.SpecFingerprintLabel, out var actualFingerprint) ||
                !string.Equals(actualFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Kubernetes operation id for job '{namespaceName}/{name}' was reused with a different request.");
            }

            return job;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static TimeSpan NormalizeRunTimeout(TimeSpan? configured)
    {
        var timeout = configured ?? TimeSpan.FromMinutes(5);
        if (timeout < TimeSpan.FromSeconds(1))
            return TimeSpan.FromSeconds(1);
        return timeout > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : timeout;
    }

    private TimeSpan ReadinessTimeout()
        => TimeSpan.FromSeconds(Math.Clamp(_options.ReadinessTimeoutSeconds, 10, 900));

    private async Task WaitForPodsReadyAsync(
        string namespaceName,
        string labelSelector,
        int expectedPods,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var pods = await _client.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: ct);
            if (pods.Items.Count(IsPodReady) >= expectedPods)
                return;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        throw new TimeoutException(
            $"Kubernetes workloads in namespace '{namespaceName}' did not become ready within {timeout.TotalSeconds:0} seconds.");
    }

    private static bool IsPodReady(V1Pod pod)
        => string.Equals(pod.Status?.Phase, "Running", StringComparison.OrdinalIgnoreCase) &&
           pod.Status?.Conditions?.Any(condition =>
               string.Equals(condition.Type, "Ready", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(condition.Status, "True", StringComparison.OrdinalIgnoreCase)) == true;

    private async Task<string?> ReadPodLogBestEffortAsync(string podName, string namespaceName, CancellationToken ct)
    {
        try
        {
            await using var stream = await _client.CoreV1.ReadNamespacedPodLogAsync(
                podName,
                namespaceName,
                limitBytes: 1_048_576,
                tailLines: 1_000,
                cancellationToken: ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read Kubernetes pod log for {Namespace}/{Pod}.", namespaceName, podName);
            return null;
        }
    }

    private async Task<V1Namespace> EnsureManagedNamespaceAsync(string namespaceName, CancellationToken ct)
    {
        var expectedPrefix = KubernetesNames.InstancePrefix(_options.NamespacePrefix);
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

        return ns;
    }

    private static void EnsureNamespaceMatchesRuntime(
        V1Namespace ns,
        Guid competitionId,
        Guid? teamId,
        Guid? challengeId,
        string? projectName)
    {
        var labels = ns.Metadata?.Labels
            ?? throw new InvalidOperationException("Kubernetes runtime namespace has no identity labels.");
        EnsureIdentityLabel(labels, "competitionId", competitionId == Guid.Empty ? null : competitionId.ToString("D"));
        EnsureIdentityLabel(labels, "teamId", teamId?.ToString("D"));
        EnsureIdentityLabel(labels, "challengeId", challengeId?.ToString("D"));
        EnsureIdentityLabel(labels, KubernetesManifestFactory.ProjectLabel, projectName);
    }

    private static void EnsureIdentityLabel(
        IDictionary<string, string> labels,
        string key,
        string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return;

        if (!labels.TryGetValue(key, out var actual) ||
            !string.Equals(actual, KubernetesNames.SafeName(expected), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Kubernetes runner refused to delete a runtime whose identity does not match the request.");
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

    private async Task DeleteNamespaceAsync(
        string namespaceName,
        CancellationToken cancellationToken,
        string? namespaceUid = null)
    {
        using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanupCts.CancelAfter(TimeSpan.FromSeconds(30));
        await _client.CoreV1.DeleteNamespaceAsync(
            namespaceName,
            body: string.IsNullOrWhiteSpace(namespaceUid)
                ? null
                : new V1DeleteOptions
                {
                    Preconditions = new V1Preconditions { Uid = namespaceUid }
                },
            cancellationToken: cleanupCts.Token);
    }

    private async Task WaitForNamespaceDeletedAsync(
        string namespaceName,
        string? namespaceUid,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var current = await _client.CoreV1.ReadNamespaceAsync(
                    namespaceName,
                    cancellationToken: ct);
                if (!string.IsNullOrWhiteSpace(namespaceUid) &&
                    !string.Equals(current.Metadata?.Uid, namespaceUid, StringComparison.Ordinal))
                {
                    return;
                }
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }

        throw new TimeoutException(
            $"Kubernetes namespace '{namespaceName}' was not deleted within {timeout.TotalSeconds:0} seconds.");
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

    private static Dictionary<string, string> WithOperationMetadata(
        IReadOnlyDictionary<string, string>? labels,
        Guid? operationId,
        string fingerprint)
    {
        var result = labels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(labels, StringComparer.Ordinal);
        foreach (var key in result.Keys.Where(key =>
                     key.Equals(KubernetesManifestFactory.OperationLabel, StringComparison.OrdinalIgnoreCase) ||
                     key.Equals(KubernetesManifestFactory.SpecFingerprintLabel, StringComparison.OrdinalIgnoreCase) ||
                     key.Equals(KubernetesManifestFactory.ProjectLabel, StringComparison.OrdinalIgnoreCase) ||
                     key.Equals(KubernetesManifestFactory.RunReceiptLabel, StringComparison.OrdinalIgnoreCase) ||
                     key.Equals(KubernetesManifestFactory.RunReceiptExpiresLabel, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            result.Remove(key);
        }
        if (operationId.HasValue)
        {
            result[KubernetesManifestFactory.OperationLabel] = operationId.Value.ToString("D");
            result[KubernetesManifestFactory.SpecFingerprintLabel] = fingerprint;
        }
        return result;
    }

    private void AddRuntimeExpiryMetadata(
        IDictionary<string, string> labels,
        Guid? operationId,
        TimeSpan? ttl)
    {
        if (!operationId.HasValue || ttl is not { } duration || duration <= TimeSpan.Zero)
            return;

        var maximumDuration = TimeSpan.FromDays(30);
        if (duration > maximumDuration)
            duration = maximumDuration;
        labels[KubernetesManifestFactory.RunReceiptLabel] = bool.TrueString;
        // Ordinary runtimes can be extended in the application database. This
        // provider-side deadline is an orphan safety net, not the user-visible
        // expiry, so retain a wide grace window.
        labels[KubernetesManifestFactory.RunReceiptExpiresLabel] = DateTimeOffset.UtcNow
            .Add(duration)
            .AddSeconds(_options.RuntimeOrphanGraceSeconds)
            .ToUnixTimeSeconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string ComputeContainerFingerprint(ContainerConfig config)
        => ComputeFingerprint(new
        {
            config.Image,
            config.Command,
            Environment = Ordered(config.EnvironmentVariables),
            Labels = OrderedUserLabels(config.Labels),
            Ports = config.PortMappings?.OrderBy(pair => pair.Key).ToArray(),
            config.NetworkName,
            config.RegistryAuth,
            TtlTicks = config.Ttl?.Ticks,
            config.ResourceLimits,
            config.SecurityPolicy,
            Entrypoint = config.Entrypoint?.ToArray(),
            config.OrchestrationJson,
            NetworkAliases = config.NetworkAliases?
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
        });

    internal static string ComputeComposeFingerprint(ComposeConfig config)
        => ComputeFingerprint(new
        {
            config.ProjectName,
            config.ComposeYaml,
            Environment = Ordered(config.EnvironmentVariables),
            Labels = OrderedUserLabels(config.Labels),
            TtlTicks = config.Ttl?.Ticks,
            config.OrchestrationJson
        });

    private static string ComputeFingerprint<T>(T request)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(request);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant()[..52];
    }

    private static KeyValuePair<string, string>[]? Ordered(IReadOnlyDictionary<string, string>? values)
        => values?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();

    private static KeyValuePair<string, string>[]? OrderedUserLabels(IReadOnlyDictionary<string, string>? values)
        => values?
            .Where(pair =>
                !pair.Key.Equals(KubernetesManifestFactory.OperationLabel, StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Equals(KubernetesManifestFactory.SpecFingerprintLabel, StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Equals(KubernetesManifestFactory.ProjectLabel, StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Equals(KubernetesManifestFactory.RunReceiptLabel, StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Equals(KubernetesManifestFactory.RunReceiptExpiresLabel, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();

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
