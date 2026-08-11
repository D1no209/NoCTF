using k8s;
using k8s.Models;
using System.Text.Json;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Callbacks;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Networking;

namespace NoCTF.Runtime.Kubernetes.Containers;

/// <summary>Runs isolated challenge pods through the Kubernetes client seam.</summary>
public sealed class KubernetesContainerLifecycle(
    IKubernetes client,
    KubernetesRuntimeOptions options) : IContainerLifecycle, IOneShotJobRunner,
    IAttachedOneShotJobRunner, IContainerSandboxLifecycle
{
    private const int ExecTimeoutExitCode = 124;
    private const string ResourceRoleLabel = "noctf.io/resource-role";
    private const string InternalServiceRole = "dns";
    private const string PublicServiceRole = "public";
    private const string PublicServiceSuffix = "-public";
    private const string PodPhaseRunning = "Running";
    private const string PodPhaseSucceeded = "Succeeded";
    private const string PodPhaseFailed = "Failed";
    private const string ServiceTypeClusterIp = "ClusterIP";
    private const string ServiceTypeNodePort = "NodePort";
    private const string JobKindPersistentRuntime = "persistent-runtime";
    private const string JobKindAwdpVerification = "awdp-verification";
    private const string JobKindAwdChecker = "awd-checker";
    private const string NetworkPurposeAwdChecker = "awd-checker";
    private const string NetworkPurposeAwdpChecker = "awdp-checker";
    private const string NetworkPurposeAwdpVerification = "awdp-verification";
    private const string NetworkPurposePersistentRuntime = "persistent-runtime";
    private const string ExternalReasonExitCode = "ExitCode";
    private const string ExecTimeoutScript = """
        duration=$1
        shift
        timed_out=0
        trap 'timed_out=1' USR1
        "$@" <&0 &
        child=$!
        (sleep "$duration"; kill -USR1 "$$" 2>/dev/null; kill -KILL "$child" 2>/dev/null) &
        watchdog=$!
        wait "$child"
        status=$?
        if [ "$timed_out" -eq 1 ]; then
          kill -KILL "$child" 2>/dev/null
          wait "$child" 2>/dev/null
          kill "$watchdog" 2>/dev/null
          wait "$watchdog" 2>/dev/null
          exit 124
        fi
        if kill "$watchdog" 2>/dev/null; then
          wait "$watchdog" 2>/dev/null
        fi
        exit "$status"
        """;

    public async Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Kubernetes)
            throw new ArgumentOutOfRangeException(nameof(request), request.Provider, "Kubernetes runtime cannot create another provider.");
        ValidateContainerRequest(request);

        var name = $"noctf-{request.OperationId:N}";
        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        labels["noctf.io/runtime-id"] = name;
        if (request.NetworkName is not null) labels["noctf.io/sandbox"] = request.NetworkName;
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/job-kind"] = JobKind(request.NetworkPurpose);
        labels["noctf.io/runtime-instance-id"] = (request.RuntimeInstanceId
            ?? request.OperationId).ToString("D");
        labels["noctf.io/generation"] = request.Generation.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        if (request.AllowInternalCallback)
            labels["noctf.io/purpose"] = CallbackPurpose(request.NetworkPurpose);
        if (request.NetworkPurpose == ContainerNetworkPurpose.PersistentRuntime
            && request.PortMappings.Count == 0)
            await EnsurePublicServiceAbsentAsync(name, request, cancellationToken);
        var pod = new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = options.Namespace,
                Labels = labels
            },
            Spec = new V1PodSpec
            {
                AutomountServiceAccountToken = false,
                EnableServiceLinks = false,
                Containers =
                [
                    new V1Container
                    {
                        Name = "challenge",
                        Image = request.Image,
                        Command = request.Command.ToList(),
                        ImagePullPolicy = options.ImagePullPolicy,
                        Env = request.Environment.Select(pair => new V1EnvVar { Name = pair.Key, Value = pair.Value }).ToList(),
                        Ports = request.ContainerPorts.Select(port => new V1ContainerPort { ContainerPort = port }).ToList(),
                        SecurityContext = new V1SecurityContext
                        {
                            AllowPrivilegeEscalation = !request.Security.NoNewPrivileges,
                            ReadOnlyRootFilesystem = request.Security.ReadonlyRootfs,
                            RunAsNonRoot = request.Security.RunAsNonRoot,
                            Capabilities = new V1Capabilities
                            {
                                Drop = request.Security.CapDrop.ToList(),
                                Add = request.Security.CapAdd?.ToList() ?? []
                            }
                        },
                        Resources = new V1ResourceRequirements
                        {
                            Limits = new Dictionary<string, ResourceQuantity>
                            {
                                ["memory"] = new(request.Limits.MemoryBytes.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture)),
                                ["cpu"] = new($"{request.Limits.NanoCpus / 1_000_000L}m")
                            }
                        }
                    }
                ],
                RestartPolicy = "Never"
            }
        };
        V1Pod? createdPod = null;
        var createdCallbackPolicies = new List<V1NetworkPolicy>();
        var createdServices = new List<V1Service>();
        try
        {
            var podCreation = await CreateOrReadBackPodAsync(
                pod,
                request,
                cancellationToken);
            if (podCreation.Created)
                createdPod = podCreation.Resource;
            if (request.AllowInternalCallback)
                _ = await EnsureInternalCallbackPolicyAsync(
                    name,
                    labels,
                    request,
                    cancellationToken,
                    createdCallbackPolicies);
            var services = await EnsureServicesAsync(
                name,
                labels,
                request,
                cancellationToken,
                createdServices);
            return new(request.OperationId, RuntimeProvider.Kubernetes, name, RuntimeStatus.Pending,
                services.PublishedPorts, options.PublicHost,
                services.InternalHost ?? $"{name}.{options.Namespace}.svc",
                RuntimeInstanceId: request.RuntimeInstanceId,
                Generation: request.Generation);
        }
        catch
        {
            using var cleanupSource = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            try
            {
                await CleanupCreatedResourcesAsync(
                    createdPod,
                    createdCallbackPolicies,
                    createdServices,
                    cleanupSource.Token);
            }
            catch
            {
                // Preserve the create failure; cleanup only targets resources confirmed created here.
            }
            throw;
        }
    }

    public async Task<ContainerReceipt> EnsureRunningAsync(
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Kubernetes)
            throw new ArgumentOutOfRangeException(nameof(request), request.Provider,
                "Kubernetes runtime cannot reconcile another provider.");
        ValidateContainerRequest(request);

        var name = $"noctf-{request.OperationId:N}";
        V1Pod? pod;
        try
        {
            pod = await client.CoreV1.ReadNamespacedPodAsync(
                name,
                options.Namespace,
                cancellationToken: cancellationToken);
        }
        catch (k8s.Autorest.HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            pod = null;
        }

        if (pod is not null)
            ValidatePod(pod, name, request);
        if (pod is null)
        {
            _ = await CreateAsync(request, cancellationToken);
        }
        else if (ToRuntimeStatus(pod.Status?.Phase) is RuntimeStatus.Stopped or RuntimeStatus.Failed)
        {
            await DestroyAsync(new ContainerReceipt(
                request.OperationId,
                RuntimeProvider.Kubernetes,
                name,
                ToRuntimeStatus(pod.Status?.Phase),
                request.PortMappings,
                options.PublicHost,
                null), cancellationToken);
            await WaitUntilDeletedAsync(
                name,
                request.OperationTimeout ?? TimeSpan.FromMinutes(2),
                request.AllowInternalCallback,
                cancellationToken);
            _ = await CreateAsync(request, cancellationToken);
        }

        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        labels["noctf.io/runtime-id"] = name;
        if (request.NetworkName is not null) labels["noctf.io/sandbox"] = request.NetworkName;
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/job-kind"] = JobKind(request.NetworkPurpose);
        labels["noctf.io/runtime-instance-id"] = (request.RuntimeInstanceId
            ?? request.OperationId).ToString("D");
        labels["noctf.io/generation"] = request.Generation.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        if (request.AllowInternalCallback)
            _ = await EnsureInternalCallbackPolicyAsync(
                name,
                labels,
                request,
                cancellationToken);
        var services = await EnsureServicesAsync(name, labels, request, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.OperationTimeout ?? TimeSpan.FromMinutes(2));
        try
        {
            await WaitUntilRunningAsync(name, timeout.Token);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await DestroyAsync(new ContainerReceipt(
                request.OperationId,
                RuntimeProvider.Kubernetes,
                name,
                RuntimeStatus.Failed,
                services.PublishedPorts,
                options.PublicHost,
                services.InternalHost), cancellationToken);
            throw new TimeoutException("Kubernetes runtime did not reach Running before the operation deadline.");
        }
        return new ContainerReceipt(
            request.OperationId,
            RuntimeProvider.Kubernetes,
            name,
            RuntimeStatus.Running,
            services.PublishedPorts,
            options.PublicHost,
            services.InternalHost ?? $"{name}.{options.Namespace}.svc",
            RuntimeInstanceId: request.RuntimeInstanceId,
            Generation: request.Generation);
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await TryDeleteAsync(() => client.CoreV1.DeleteNamespacedServiceAsync(
            $"{receipt.ResourceId}{PublicServiceSuffix}",
            options.Namespace,
            body: new V1DeleteOptions(),
            cancellationToken: cancellationToken));
        await TryDeleteAsync(() => client.CoreV1.DeleteNamespacedServiceAsync(
            receipt.ResourceId,
            options.Namespace,
            body: new V1DeleteOptions(),
            cancellationToken: cancellationToken));
        await TryDeleteAsync(() => client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                $"{receipt.ResourceId}-callback",
                options.Namespace,
                body: new V1DeleteOptions(),
                cancellationToken: cancellationToken));
        await TryDeleteAsync(() => client.CoreV1.DeleteNamespacedPodAsync(
                receipt.ResourceId,
                options.Namespace,
                body: new V1DeleteOptions { PropagationPolicy = "Foreground" },
                cancellationToken: cancellationToken));
        if (failures.Count > 0)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();

        async Task TryDeleteAsync(Func<Task> delete)
        {
            try
            {
                await delete();
            }
            catch (k8s.Autorest.HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The deterministic resource is already absent.
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    public async Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var receipt = await CreateAsync(request, cancellationToken);
        try
        {
            while (true)
            {
                var pod = await client.CoreV1.ReadNamespacedPodAsync(
                    receipt.ResourceId, options.Namespace, cancellationToken: cancellationToken);
                if (pod.Status?.Phase is PodPhaseSucceeded or PodPhaseFailed)
                {
                    var terminated = pod.Status.ContainerStatuses?.SingleOrDefault()?.State?.Terminated;
                    return new(receipt.ResourceId, checked((int)(terminated?.ExitCode ?? -1)), string.Empty, string.Empty,
                        startedAt, DateTimeOffset.UtcNow);
                }
                await Task.Delay(250, cancellationToken);
            }
        }
        finally
        {
            using var cleanupSource = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            await DestroyAsync(receipt, cleanupSource.Token);
        }
    }

    public async Task<OneShotResult> RunAttachedAsync(
        ContainerRequest request,
        AttachedRuntimeTarget target,
        CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Kubernetes)
            throw new InvalidOperationException(
                "An attached Kubernetes job requires the Kubernetes provider.");
        string? sandbox = null;
        switch (target)
        {
            case AttachedContainerRuntimeTarget container:
                sandbox = await ValidateContainerTargetAsync(container, cancellationToken);
                break;
            case AttachedComposeRuntimeTarget compose:
                await ValidateComposeTargetAsync(compose, cancellationToken);
                break;
            default:
                throw new InvalidOperationException("The attached Runtime target is unsupported.");
        }
        var labels = new Dictionary<string, string>(request.Labels, StringComparer.Ordinal)
        {
            ["noctf.io/managed"] = "true",
            ["noctf.io/runtime-instance-id"] = target.Identity.RuntimeInstanceId.ToString("D"),
            ["noctf.io/generation"] = target.Identity.Generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["noctf.io/job-kind"] = JobKindAwdChecker,
            ["noctf.io/purpose"] = NetworkPurposeAwdChecker
        };
        return await RunAsync(
            request with
            {
                Labels = labels,
                NetworkName = sandbox,
                AllowInternalCallback = true,
                Generation = target.Identity.Generation,
                RuntimeInstanceId = target.Identity.RuntimeInstanceId,
                NetworkPurpose = ContainerNetworkPurpose.AwdChecker
            },
            cancellationToken);
    }

    private async Task<string> ValidateContainerTargetAsync(
        AttachedContainerRuntimeTarget target,
        CancellationToken cancellationToken)
    {
        var receipt = target.Receipt;
        if (receipt.Provider != RuntimeProvider.Kubernetes
            || receipt.RuntimeInstanceId != target.Identity.RuntimeInstanceId
            || receipt.Generation != target.Identity.Generation
            || string.IsNullOrWhiteSpace(receipt.NetworkId)
            || string.IsNullOrWhiteSpace(receipt.ResourceId))
            throw new InvalidOperationException(
                "The attached Container receipt has a different ownership identity.");
        var policy = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
            receipt.NetworkId,
            options.Namespace,
            cancellationToken: cancellationToken);
        var pod = await client.CoreV1.ReadNamespacedPodAsync(
            receipt.ResourceId,
            options.Namespace,
            cancellationToken: cancellationToken);
        if (!HasResourceIdentity(policy.Metadata.Labels, target.Identity)
            || !HasNetworkPurpose(policy.Metadata.Labels, NetworkPurposePersistentRuntime)
            || !HasResourceIdentity(pod.Metadata.Labels, target.Identity)
            || !HasLabel(pod.Metadata.Labels, "noctf.io/sandbox", receipt.NetworkId))
            throw new InvalidOperationException(
                "The attached Container resources have a different ownership identity.");
        return receipt.NetworkId;
    }

    private async Task ValidateComposeTargetAsync(
        AttachedComposeRuntimeTarget target,
        CancellationToken cancellationToken)
    {
        var receipt = target.Receipt;
        if (receipt.Provider != RuntimeProvider.Kubernetes
            || receipt.OperationId != target.Identity.RuntimeInstanceId
            || receipt.Generation != target.Identity.Generation
            || !string.Equals(receipt.Namespace, options.Namespace, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(target.ServiceName))
            throw new InvalidOperationException(
                "The attached Compose receipt has a different ownership identity.");
        var selector = $"noctf.io/managed=true,"
            + $"noctf.io/runtime-instance-id={target.Identity.RuntimeInstanceId:D},"
            + $"noctf.io/generation={target.Identity.Generation},"
            + $"noctf.io/compose-service={target.ServiceName}";
        var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        if (deployments.Items.Count != 1
            || !HasResourceIdentity(deployments.Items[0].Metadata.Labels, target.Identity))
            throw new InvalidOperationException(
                "The attached Compose service was not found with the required ownership identity.");
    }

    public async Task<string> CreateIsolatedNetworkAsync(
        ContainerNetworkPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var identity = request.Identity;
        if (identity.RuntimeInstanceId == Guid.Empty
            || identity.Generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!Enum.IsDefined(request.Purpose)
            || !Enum.IsDefined(request.EgressPolicy)
            || request.PublicIngressPorts.Any(port => port is < 1 or > 65535))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Purpose == ContainerNetworkPurpose.AwdpVerification
            && request.TargetPort is not (>= 1 and <= 65535))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!options.NetworkPolicyRequired)
            throw new InvalidOperationException(
                "Kubernetes container runtimes require NetworkPolicy enforcement.");
        var name = request.Purpose == ContainerNetworkPurpose.AwdpVerification
            ? $"noctf-awdp-{identity.RuntimeInstanceId:N}"
            : $"noctf-rt-{identity.RuntimeInstanceId:N}-{identity.Generation}";
        var purpose = request.Purpose == ContainerNetworkPurpose.AwdpVerification
            ? NetworkPurposeAwdpVerification
            : NetworkPurposePersistentRuntime;
        try
        {
            var existing = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                name,
                options.Namespace,
                cancellationToken: cancellationToken);
            if (!HasResourceIdentity(existing.Metadata.Labels, identity)
                || !HasNetworkPurpose(existing.Metadata.Labels, purpose))
                throw new InvalidOperationException(
                    "The existing runtime policy has a different ownership identity or purpose.");
            return name;
        }
        catch (k8s.Autorest.HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The deterministic policy has not been created for this operation yet.
        }
        var labels = new Dictionary<string, string>
        {
            ["noctf.io/job-kind"] = purpose,
            ["noctf.io/network-purpose"] = purpose,
            ["noctf.io/managed"] = "true",
            ["noctf.io/runtime-instance-id"] = identity.RuntimeInstanceId.ToString("D"),
            ["noctf.io/generation"] = identity.Generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        };
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["noctf.io/sandbox"] = name }
        };
        var ingress = new List<V1NetworkPolicyIngressRule>
        {
            new()
            {
                FromProperty = [new V1NetworkPolicyPeer { PodSelector = selector }],
                Ports = request.Purpose == ContainerNetworkPurpose.AwdpVerification
                    ? [new V1NetworkPolicyPort { Protocol = "TCP", Port = request.TargetPort!.Value }]
                    : null
            }
        };
        if (request.Purpose == ContainerNetworkPurpose.PersistentRuntime
            && request.PublicIngressPorts.Count > 0)
        {
            ingress.Add(new V1NetworkPolicyIngressRule
            {
                Ports = request.PublicIngressPorts
                    .Distinct()
                    .Order()
                    .Select(port => new V1NetworkPolicyPort
                    {
                        Protocol = "TCP",
                        Port = port
                    })
                    .ToList()
            });
        }
        try
        {
            await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(new V1NetworkPolicy
            {
                Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = options.Namespace, Labels = labels },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = selector,
                    PolicyTypes = ["Ingress", "Egress"],
                    Ingress = ingress,
                    Egress = KubernetesEgressPolicy.Build(
                        request.EgressPolicy,
                        selector,
                        options).ToList()
                }
            }, options.Namespace, cancellationToken: cancellationToken);
            return name;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            try
            {
                var ambiguous = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                    name,
                    options.Namespace,
                    cancellationToken: cleanup.Token);
                if (HasResourceIdentity(ambiguous.Metadata.Labels, identity)
                    && HasNetworkPurpose(ambiguous.Metadata.Labels, purpose))
                    await DeleteIsolatedNetworkAsync(name, cleanup.Token);
            }
            catch (k8s.Autorest.HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The server did not create the policy.
            }
            catch
            {
                // The ambiguous network policy could not be safely cleaned up.
            }
            throw;
        }
    }

    public async Task DeleteIsolatedNetworkAsync(string networkId, CancellationToken cancellationToken)
    {
        try
        {
            await client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(networkId, options.Namespace,
                body: new V1DeleteOptions(), cancellationToken: cancellationToken);
        }
        catch (k8s.Autorest.HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Deletion is idempotent.
        }
    }

    public async Task CopyArchiveAsync(
        ContainerReceipt receipt, Stream tarArchive, CancellationToken cancellationToken)
    {
        await WaitUntilRunningAsync(receipt.ResourceId, cancellationToken);
        using var demuxer = await client.MuxedStreamNamespacedPodExecAsync(
            receipt.ResourceId, options.Namespace, ["/bin/sh", "-c", "mkdir -p /noctf/fix && tar -xf - -C /"],
            "challenge", true, false, false, false, cancellationToken: cancellationToken);
        demuxer.Start();
        using var standardInput = demuxer.GetStream(null, ChannelIndex.StdIn);
        using var error = demuxer.GetStream(ChannelIndex.Error, null);
        var statusTask = ReadExecStatusAsync(error, cancellationToken);
        await tarArchive.CopyToAsync(standardInput, cancellationToken);
        await standardInput.FlushAsync(cancellationToken);
        standardInput.Dispose();
        var exitCode = await statusTask;
        if (exitCode != 0) throw new InvalidOperationException("Kubernetes could not extract the Fix archive.");
    }

    public async Task<ContainerExecResult> ExecAsync(
        ContainerReceipt receipt,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await RunExecAsync(receipt, command, null, timeout, cancellationToken);

    public async Task<ContainerExecResult> ExecWithInputAsync(
        ContainerReceipt receipt,
        IReadOnlyList<string> command,
        ReadOnlyMemory<byte> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await RunExecAsync(receipt, command, standardInput, timeout, cancellationToken);

    private async Task<ContainerExecResult> RunExecAsync(
        ContainerReceipt receipt,
        IReadOnlyList<string> command,
        ReadOnlyMemory<byte>? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout + TimeSpan.FromSeconds(3));
        try
        {
            await WaitUntilRunningAsync(receipt.ResourceId, timeoutSource.Token);
            using var demuxer = await client.MuxedStreamNamespacedPodExecAsync(
                receipt.ResourceId, options.Namespace, WithTimeout(command, timeout), "challenge",
                standardInput is not null, false, false, false, cancellationToken: timeoutSource.Token);
            demuxer.Start();
            using var error = demuxer.GetStream(ChannelIndex.Error, null);
            var statusTask = ReadExecStatusAsync(error, timeoutSource.Token);
            if (standardInput is { } inputMemory)
            {
                using var input = demuxer.GetStream(null, ChannelIndex.StdIn);
                await input.WriteAsync(inputMemory, timeoutSource.Token);
                await input.FlushAsync(timeoutSource.Token);
            }
            var exitCode = await statusTask.WaitAsync(timeoutSource.Token);
            if (exitCode != ExecTimeoutExitCode) return new(exitCode, false);
            await StopPodAfterExecTimeoutAsync(receipt);
            return new(-1, true);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            await StopPodAfterExecTimeoutAsync(receipt);
            cancellationToken.ThrowIfCancellationRequested();
            return new(-1, true);
        }
    }

    private static IReadOnlyList<string> WithTimeout(IReadOnlyList<string> command, TimeSpan timeout)
    {
        var seconds = Math.Max(1, checked((int)Math.Ceiling(timeout.TotalSeconds)));
        return ["/bin/sh", "-c", ExecTimeoutScript, "noctf-exec",
            seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), .. command];
    }

    private async Task StopPodAfterExecTimeoutAsync(ContainerReceipt receipt)
    {
        using var cleanupSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await DestroyAsync(receipt, cleanupSource.Token);
    }

    public async Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken)
    {
        if (provider != RuntimeProvider.Kubernetes)
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Kubernetes runtime cannot query another provider.");
        try
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(resourceId, options.Namespace, cancellationToken: cancellationToken);
            var phase = ToRuntimeStatus(pod.Status?.Phase);
            return new(Guid.Empty, RuntimeProvider.Kubernetes, resourceId, phase, new Dictionary<int, int>(), options.PublicHost,
                $"{resourceId}.{options.Namespace}.svc");
        }
        catch (k8s.Autorest.HttpOperationException exception) when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static RuntimeStatus ToRuntimeStatus(string? phase) => phase?.ToLowerInvariant() switch
    {
        "pending" => RuntimeStatus.Pending,
        "running" => RuntimeStatus.Running,
        "succeeded" => RuntimeStatus.Stopped,
        "failed" => RuntimeStatus.Failed,
        _ => RuntimeStatus.Failed
    };

    private async Task<CreatedResource<V1Pod>> CreateOrReadBackPodAsync(
        V1Pod desired,
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await client.CoreV1.CreateNamespacedPodAsync(
                desired,
                options.Namespace,
                cancellationToken: cancellationToken);
            created.Metadata ??= new V1ObjectMeta();
            if (string.IsNullOrWhiteSpace(created.Metadata.Name))
                created.Metadata.Name = desired.Metadata.Name;
            return new(created, true);
        }
        catch (Exception creationException) when (!cancellationToken.IsCancellationRequested)
        {
            V1Pod existing;
            try
            {
                existing = await client.CoreV1.ReadNamespacedPodAsync(
                    desired.Metadata.Name,
                    options.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(creationException)
                    .Throw();
                throw;
            }
            ValidatePod(existing, desired.Metadata.Name, request);
            return new(existing, false);
        }
    }

    private async Task CleanupCreatedResourcesAsync(
        V1Pod? pod,
        IReadOnlyList<V1NetworkPolicy> callbackPolicies,
        IReadOnlyList<V1Service> services,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var service in services.Reverse())
        {
            if (string.IsNullOrWhiteSpace(service.Metadata?.Name))
                continue;
            await TryDeleteAsync(() => client.CoreV1.DeleteNamespacedServiceAsync(
                service.Metadata.Name,
                options.Namespace,
                body: CreatedDeleteOptions(service.Metadata),
                cancellationToken: cancellationToken));
        }
        foreach (var callbackPolicy in callbackPolicies.Reverse())
        {
            if (string.IsNullOrWhiteSpace(callbackPolicy.Metadata?.Name))
                continue;
            await TryDeleteAsync(() => client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                callbackPolicy.Metadata.Name,
                options.Namespace,
                body: CreatedDeleteOptions(callbackPolicy.Metadata),
                cancellationToken: cancellationToken));
        }
        if (!string.IsNullOrWhiteSpace(pod?.Metadata?.Name))
        {
            await TryDeleteAsync(() => client.CoreV1.DeleteNamespacedPodAsync(
                pod.Metadata.Name,
                options.Namespace,
                body: CreatedDeleteOptions(pod.Metadata, "Foreground"),
                cancellationToken: cancellationToken));
        }
        if (failures.Count > 0)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();

        async Task TryDeleteAsync(Func<Task> delete)
        {
            try
            {
                await delete();
            }
            catch (k8s.Autorest.HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound
                    || exception.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The created resource is absent or its UID precondition no longer matches.
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private static V1DeleteOptions CreatedDeleteOptions(
        V1ObjectMeta metadata,
        string? propagationPolicy = null)
    {
        var delete = new V1DeleteOptions { PropagationPolicy = propagationPolicy };
        if (string.IsNullOrWhiteSpace(metadata.Uid))
            throw new InvalidOperationException(
                "A created Kubernetes resource cannot be safely deleted without its UID.");
        delete.Preconditions = new V1Preconditions { Uid = metadata.Uid };
        return delete;
    }

    private async Task<ContainerServices> EnsureServicesAsync(
        string name,
        IReadOnlyDictionary<string, string> labels,
        ContainerRequest request,
        CancellationToken cancellationToken,
        ICollection<V1Service>? createdServices = null)
    {
        if (request.NetworkPurpose == ContainerNetworkPurpose.PersistentRuntime
            && request.PortMappings.Count == 0)
            await EnsurePublicServiceAbsentAsync(name, request, cancellationToken);
        if (request.ContainerPorts.Count == 0)
            return new(null, new Dictionary<int, int>());
        if (request.NetworkName is null)
            throw new InvalidOperationException(
                "A Kubernetes Container service requires an isolated Runtime network.");

        var identity = new RuntimeResourceIdentity(
            request.RuntimeInstanceId ?? request.OperationId,
            request.Generation);
        var internalService = await EnsureServiceAsync(
            name,
            name,
            labels,
            identity,
            InternalServiceRole,
            ServiceTypeClusterIp,
            request.ContainerPorts,
            requireAssignedNodePorts: false,
            cancellationToken,
            createdServices);
        IReadOnlyDictionary<int, int> publishedPorts = new Dictionary<int, int>();
        if (request.PortMappings.Count > 0)
        {
            var publicService = await EnsureServiceAsync(
                $"{name}{PublicServiceSuffix}",
                name,
                labels,
                identity,
                PublicServiceRole,
                ServiceTypeNodePort,
                request.PortMappings.Keys.Order().ToArray(),
                requireAssignedNodePorts: true,
                cancellationToken,
                createdServices);
            publishedPorts = publicService.Spec.Ports.ToDictionary(
                port => port.Port,
                port => port.NodePort!.Value);
        }
        return new(internalService.Spec.ClusterIP, publishedPorts);
    }

    private async Task<V1Service> EnsureServiceAsync(
        string name,
        string podName,
        IReadOnlyDictionary<string, string> labels,
        RuntimeResourceIdentity identity,
        string role,
        string type,
        IReadOnlyList<int> ports,
        bool requireAssignedNodePorts,
        CancellationToken cancellationToken,
        ICollection<V1Service>? createdServices)
    {
        try
        {
            var existing = await client.CoreV1.ReadNamespacedServiceAsync(
                name,
                options.Namespace,
                cancellationToken: cancellationToken);
            var selector = ServiceSelector(podName, identity, JobKindFromLabels(labels));
            ValidateService(
                existing,
                name,
                identity,
                role,
                type,
                selector,
                ports,
                requireAssignedNodePorts);
            return existing;
        }
        catch (k8s.Autorest.HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var jobKind = JobKindFromLabels(labels);
            var selector = ServiceSelector(podName, identity, jobKind);
            var serviceLabels = PlatformServiceLabels(
                labels,
                podName,
                identity,
                jobKind,
                role);
            var desired = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = name,
                    NamespaceProperty = options.Namespace,
                    Labels = serviceLabels
                },
                Spec = new V1ServiceSpec
                {
                    Selector = selector,
                    Ports = ports.Select(port => new V1ServicePort
                    {
                        Name = $"tcp-{port}",
                        Port = port,
                        TargetPort = port,
                        Protocol = "TCP"
                    }).ToList(),
                    Type = type
                }
            };
            V1Service service;
            try
            {
                service = await client.CoreV1.CreateNamespacedServiceAsync(
                    desired,
                    options.Namespace,
                    cancellationToken: cancellationToken);
                createdServices?.Add(service);
            }
            catch (Exception creationException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    service = await client.CoreV1.ReadNamespacedServiceAsync(
                        name,
                        options.Namespace,
                        cancellationToken: cancellationToken);
                }
                catch
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(creationException)
                        .Throw();
                    throw;
                }
            }
            ValidateService(
                service,
                name,
                identity,
                role,
                type,
                selector,
                ports,
                requireAssignedNodePorts);
            return service;
        }
    }

    private static void ValidateService(
        V1Service service,
        string name,
        RuntimeResourceIdentity identity,
        string role,
        string type,
        IReadOnlyDictionary<string, string> expectedSelector,
        IReadOnlyList<int> expectedPorts,
        bool requireAssignedNodePorts)
    {
        var spec = service.Spec
            ?? throw new InvalidOperationException(
                $"Kubernetes Service '{name}' has no specification.");
        if (!string.Equals(service.Metadata?.Name, name, StringComparison.Ordinal)
            || !HasResourceIdentity(service.Metadata?.Labels, identity)
            || !HasLabel(service.Metadata?.Labels, ResourceRoleLabel, role)
            || !HasLabel(
                service.Metadata?.Labels,
                "noctf.io/job-kind",
                expectedSelector["noctf.io/job-kind"])
            || !HasLabel(
                service.Metadata?.Labels,
                "noctf.io/runtime-id",
                expectedSelector["noctf.io/runtime-id"])
            || !string.Equals(spec.Type, type, StringComparison.Ordinal)
            || !HasExactLabels(spec.Selector, expectedSelector)
            || spec.ExternalIPs?.Count > 0
            || !string.IsNullOrWhiteSpace(spec.ExternalName)
            || !string.IsNullOrWhiteSpace(spec.LoadBalancerIP))
            throw new InvalidOperationException(
                $"Kubernetes Service '{name}' has a different ownership identity or role.");

        var expected = expectedPorts.Order().ToArray();
        var actual = spec.Ports?.OrderBy(port => port.Port).ToArray() ?? [];
        if (actual.Length != expected.Length
            || !actual.Select(port => port.Port).SequenceEqual(expected))
            throw new InvalidOperationException(
                $"Kubernetes Service '{name}' has a different port contract.");
        foreach (var port in actual)
        {
            if (!string.Equals(port.Name, $"tcp-{port.Port}", StringComparison.Ordinal)
                || port.TargetPort?.Value
                    != port.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || !string.Equals(port.Protocol, "TCP", StringComparison.Ordinal)
                || type == ServiceTypeClusterIp && port.NodePort is not null
                || requireAssignedNodePorts && port.NodePort is not (>= 1 and <= 65535))
                throw new InvalidOperationException(
                    $"Kubernetes Service '{name}' has a different port contract.");
        }
        if (type == ServiceTypeClusterIp
            && (string.IsNullOrWhiteSpace(spec.ClusterIP)
                || string.Equals(spec.ClusterIP, "None", StringComparison.OrdinalIgnoreCase))
            || requireAssignedNodePorts
                && actual.Select(port => port.NodePort!.Value).Distinct().Count()
                    != actual.Length)
            throw new InvalidOperationException(
                $"Kubernetes Service '{name}' has a different port contract.");
    }

    private static void ValidateContainerRequest(ContainerRequest request)
    {
        var identity = new RuntimeResourceIdentity(
            request.RuntimeInstanceId ?? request.OperationId,
            request.Generation);
        if (identity.RuntimeInstanceId == Guid.Empty || identity.Generation <= 0)
            throw new InvalidOperationException(
                "A Kubernetes Container requires a valid Runtime identity.");
        if (request.ContainerPorts.Any(port => port is < 1 or > 65535)
            || request.PortMappings.Any(mapping => mapping.Value != 0))
            throw new InvalidOperationException(
                "Kubernetes Container public ports require a valid container port and dynamic NodePort allocation.");
        if (request.ContainerPorts.Count == 0)
            return;
        if (request.NetworkName is null)
            throw new InvalidOperationException(
                "A Kubernetes Container service requires a valid Runtime network identity.");
        if (request.PortMappings.Count > 0
            && request.NetworkPurpose != ContainerNetworkPurpose.PersistentRuntime)
            throw new InvalidOperationException(
                "Only a persistent Kubernetes Container Runtime may publish NodePorts.");
    }

    private async Task EnsurePublicServiceAbsentAsync(
        string podName,
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        V1Service existing;
        try
        {
            existing = await client.CoreV1.ReadNamespacedServiceAsync(
                $"{podName}{PublicServiceSuffix}",
                options.Namespace,
                cancellationToken: cancellationToken);
        }
        catch (k8s.Autorest.HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }
        var identity = new RuntimeResourceIdentity(
            request.RuntimeInstanceId ?? request.OperationId,
            request.Generation);
        var publicName = $"{podName}{PublicServiceSuffix}";
        var metadata = existing.Metadata;
        if (metadata is null
            || !string.Equals(metadata.Name, publicName, StringComparison.Ordinal)
            || !HasResourceIdentity(metadata.Labels, identity)
            || !HasLabel(metadata.Labels, ResourceRoleLabel, PublicServiceRole)
            || !HasLabel(
                metadata.Labels,
                "noctf.io/job-kind",
                JobKind(request.NetworkPurpose))
            || !HasLabel(metadata.Labels, "noctf.io/runtime-id", podName))
            throw new InvalidOperationException(
                $"Kubernetes Service '{publicName}' has a different ownership identity or role.");
        await client.CoreV1.DeleteNamespacedServiceAsync(
            publicName,
            options.Namespace,
            body: CreatedDeleteOptions(metadata),
            cancellationToken: cancellationToken);
        var deadline = DateTimeOffset.UtcNow.Add(
            request.OperationTimeout ?? TimeSpan.FromMinutes(2));
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                _ = await client.CoreV1.ReadNamespacedServiceAsync(
                    publicName,
                    options.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch (k8s.Autorest.HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        throw new TimeoutException(
            $"Kubernetes Service '{publicName}' was not deleted before reconciliation.");
    }

    private static void ValidatePod(V1Pod pod, string name, ContainerRequest request)
    {
        var identity = new RuntimeResourceIdentity(
            request.RuntimeInstanceId ?? request.OperationId,
            request.Generation);
        if (!string.Equals(pod.Metadata?.Name, name, StringComparison.Ordinal)
            || !HasResourceIdentity(pod.Metadata?.Labels, identity)
            || !HasLabel(pod.Metadata?.Labels, "noctf.io/runtime-id", name)
            || !HasLabel(
                pod.Metadata?.Labels,
                "noctf.io/job-kind",
                JobKind(request.NetworkPurpose))
            || request.NetworkName is not null
                && !HasLabel(
                    pod.Metadata?.Labels,
                    "noctf.io/sandbox",
                    request.NetworkName)
            || request.AllowInternalCallback
                && !HasLabel(
                    pod.Metadata?.Labels,
                    "noctf.io/purpose",
                    CallbackPurpose(request.NetworkPurpose)))
            throw new InvalidOperationException(
                $"Kubernetes Pod '{name}' has a different ownership identity or network.");
    }

    private static Dictionary<string, string> ServiceSelector(
        string podName,
        RuntimeResourceIdentity identity,
        string jobKind) =>
        new(StringComparer.Ordinal)
        {
            ["noctf.io/runtime-id"] = podName,
            ["noctf.io/managed"] = "true",
            ["noctf.io/job-kind"] = jobKind,
            ["noctf.io/runtime-instance-id"] = identity.RuntimeInstanceId.ToString("D"),
            ["noctf.io/generation"] = identity.Generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        };

    private static Dictionary<string, string> PlatformServiceLabels(
        IReadOnlyDictionary<string, string> configured,
        string podName,
        RuntimeResourceIdentity identity,
        string jobKind,
        string role)
    {
        var labels = configured.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in ServiceSelector(podName, identity, jobKind))
            labels[pair.Key] = pair.Value;
        labels[ResourceRoleLabel] = role;
        return labels;
    }

    private static string JobKindFromLabels(IReadOnlyDictionary<string, string> labels) =>
        labels.TryGetValue("noctf.io/job-kind", out var jobKind)
            && jobKind is JobKindPersistentRuntime or JobKindAwdpVerification or JobKindAwdChecker
                ? jobKind
                : throw new InvalidOperationException(
                    "A Kubernetes Container service requires a supported job kind.");

    private static bool HasExactLabels(
        IDictionary<string, string>? actual,
        IReadOnlyDictionary<string, string> expected) =>
        actual is not null
        && actual.Count == expected.Count
        && expected.All(pair => HasLabel(actual, pair.Key, pair.Value));

    private async Task<CreatedResource<V1NetworkPolicy>> EnsureInternalCallbackPolicyAsync(
        string name,
        IReadOnlyDictionary<string, string> labels,
        ContainerRequest request,
        CancellationToken cancellationToken,
        ICollection<V1NetworkPolicy>? createdPolicies = null)
    {
        var callbackPort = GetCallbackPort(request);
        var purpose = CallbackPurpose(request.NetworkPurpose);
        var policyName = $"{name}-callback";
        var desired = new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta
            {
                Name = policyName,
                NamespaceProperty = options.Namespace,
                Labels = labels.ToDictionary(pair => pair.Key, pair => pair.Value)
            },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string>
                    {
                        ["noctf.io/runtime-id"] = name,
                        ["noctf.io/purpose"] = purpose
                    }
                },
                PolicyTypes = ["Egress"],
                Egress =
                [
                    new V1NetworkPolicyEgressRule
                    {
                        To =
                        [
                            new V1NetworkPolicyPeer
                            {
                                NamespaceSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>
                                    {
                                        [options.CallbackNamespaceLabelKey] =
                                            options.CallbackNamespaceLabelValue
                                    }
                                },
                                PodSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>
                                    {
                                        [options.CallbackPodLabelKey] = options.CallbackPodLabelValue
                                    }
                                }
                            }
                        ],
                        Ports =
                        [
                            new V1NetworkPolicyPort
                            {
                                Protocol = "TCP",
                                Port = callbackPort
                            }
                        ]
                    },
                    new V1NetworkPolicyEgressRule
                    {
                        To =
                        [
                            new V1NetworkPolicyPeer
                            {
                                NamespaceSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>
                                    {
                                        ["kubernetes.io/metadata.name"] = "kube-system"
                                    }
                                },
                                PodSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string>
                                    {
                                        ["k8s-app"] = "kube-dns"
                                    }
                                }
                            }
                        ],
                        Ports =
                        [
                            new V1NetworkPolicyPort
                            {
                                Protocol = "UDP",
                                Port = 53
                            },
                            new V1NetworkPolicyPort
                            {
                                Protocol = "TCP",
                                Port = 53
                            }
                        ]
                    }
                ]
            }
        };
        try
        {
            var existing = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                policyName,
                options.Namespace,
                cancellationToken: cancellationToken);
            ValidateCallbackPolicy(existing, name, request, purpose, callbackPort);
            return new(existing, false);
        }
        catch (k8s.Autorest.HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The deterministic callback policy has not been created yet.
        }

        V1NetworkPolicy policy;
        try
        {
            policy = await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
                desired,
                options.Namespace,
                cancellationToken: cancellationToken);
            createdPolicies?.Add(policy);
        }
        catch (Exception creationException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                policy = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                    policyName,
                    options.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(creationException)
                    .Throw();
                throw;
            }
            ValidateCallbackPolicy(policy, name, request, purpose, callbackPort);
            return new(policy, false);
        }
        ValidateCallbackPolicy(policy, name, request, purpose, callbackPort);
        return new(policy, true);
    }

    private static string CallbackPurpose(ContainerNetworkPurpose purpose) => purpose switch
    {
        ContainerNetworkPurpose.AwdChecker => NetworkPurposeAwdChecker,
        ContainerNetworkPurpose.AwdpVerification => NetworkPurposeAwdpChecker,
        _ => throw new InvalidOperationException(
            "Only scoring checker containers can request an internal callback.")
    };

    private void ValidateCallbackPolicy(
        V1NetworkPolicy policy,
        string podName,
        ContainerRequest request,
        string purpose,
        int callbackPort)
    {
        var identity = new RuntimeResourceIdentity(
            request.RuntimeInstanceId ?? request.OperationId,
            request.Generation);
        var spec = policy.Spec;
        if (!string.Equals(
                policy.Metadata?.Name,
                $"{podName}-callback",
                StringComparison.Ordinal)
            || !HasResourceIdentity(policy.Metadata?.Labels, identity)
            || !HasLabel(
                policy.Metadata?.Labels,
                "noctf.io/job-kind",
                JobKind(request.NetworkPurpose))
            || !HasLabel(policy.Metadata?.Labels, "noctf.io/purpose", purpose)
            || spec is null
            || !HasExactSelector(
                spec.PodSelector,
                new Dictionary<string, string>
                {
                    ["noctf.io/runtime-id"] = podName,
                    ["noctf.io/purpose"] = purpose
                })
            || spec.PolicyTypes?.Count != 1
            || !string.Equals(spec.PolicyTypes[0], "Egress", StringComparison.Ordinal)
            || spec.Ingress?.Count > 0
            || spec.Egress?.Count != 2
            || spec.Egress.Count(rule =>
                IsCallbackRule(rule, callbackPort)) != 1
            || spec.Egress.Count(IsDnsRule) != 1)
            throw new InvalidOperationException(
                $"Kubernetes NetworkPolicy '{podName}-callback' has a different ownership identity or callback contract.");
    }

    private bool IsCallbackRule(
        V1NetworkPolicyEgressRule rule,
        int callbackPort)
    {
        if (rule.To?.Count != 1 || rule.Ports?.Count != 1)
            return false;
        var peer = rule.To[0];
        var port = rule.Ports[0];
        return peer.IpBlock is null
            && HasExactSelector(
                peer.NamespaceSelector,
                new Dictionary<string, string>
                {
                    [options.CallbackNamespaceLabelKey] =
                        options.CallbackNamespaceLabelValue
                })
            && HasExactSelector(
                peer.PodSelector,
                new Dictionary<string, string>
                {
                    [options.CallbackPodLabelKey] = options.CallbackPodLabelValue
                })
            && string.Equals(port.Protocol, "TCP", StringComparison.Ordinal)
            && port.EndPort is null
            && port.Port?.Value
                == callbackPort.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsDnsRule(V1NetworkPolicyEgressRule rule)
    {
        if (rule.To?.Count != 1 || rule.Ports?.Count != 2)
            return false;
        var peer = rule.To[0];
        return peer.IpBlock is null
            && HasExactSelector(
                peer.NamespaceSelector,
                new Dictionary<string, string>
                {
                    ["kubernetes.io/metadata.name"] = "kube-system"
                })
            && HasExactSelector(
                peer.PodSelector,
                new Dictionary<string, string>
                {
                    ["k8s-app"] = "kube-dns"
                })
            && rule.Ports.All(port => port.EndPort is null)
            && rule.Ports
                .Select(port => $"{port.Protocol}:{port.Port?.Value}")
                .Order(StringComparer.Ordinal)
                .SequenceEqual(["TCP:53", "UDP:53"]);
    }

    private static bool HasExactSelector(
        V1LabelSelector? selector,
        IReadOnlyDictionary<string, string> expectedLabels) =>
        selector is not null
        && selector.MatchExpressions is null or { Count: 0 }
        && HasExactLabels(selector.MatchLabels, expectedLabels);

    private static int GetCallbackPort(ContainerRequest request)
    {
        if (!request.Environment.TryGetValue("NOCTF_CALLBACK_URL", out var callbackText)
            || !Uri.TryCreate(callbackText, UriKind.Absolute, out var callback)
            || callback.Scheme is not ("http" or "https"))
            throw new InvalidOperationException(
                "A scoring checker callback requires an absolute HTTP(S) callback URL.");
        return callback.Port;
    }

    private async Task WaitUntilDeletedAsync(
        string name,
        TimeSpan timeout,
        bool waitForCallbackPolicy,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var podExists = true;
            var serviceExists = true;
            var publicServiceExists = true;
            var callbackPolicyExists = waitForCallbackPolicy;
            try
            {
                _ = await client.CoreV1.ReadNamespacedPodAsync(
                    name,
                    options.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch (k8s.Autorest.HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                podExists = false;
            }
            try
            {
                _ = await client.CoreV1.ReadNamespacedServiceAsync(
                    name,
                    options.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch (k8s.Autorest.HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                serviceExists = false;
            }
            try
            {
                _ = await client.CoreV1.ReadNamespacedServiceAsync(
                    $"{name}{PublicServiceSuffix}",
                    options.Namespace,
                    cancellationToken: cancellationToken);
            }
            catch (k8s.Autorest.HttpOperationException exception)
                when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                publicServiceExists = false;
            }
            if (waitForCallbackPolicy)
            {
                try
                {
                    _ = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(
                        $"{name}-callback",
                        options.Namespace,
                        cancellationToken: cancellationToken);
                }
                catch (k8s.Autorest.HttpOperationException exception)
                    when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    callbackPolicyExists = false;
                }
            }
            if (!podExists
                && !serviceExists
                && !publicServiceExists
                && !callbackPolicyExists)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        throw new TimeoutException("Kubernetes terminal resources were not deleted before recreation.");
    }

    private sealed record ContainerServices(
        string? InternalHost,
        IReadOnlyDictionary<int, int> PublishedPorts);

    private sealed record CreatedResource<T>(T Resource, bool Created);

    private async Task WaitUntilRunningAsync(string podName, CancellationToken cancellationToken)
    {
        while (true)
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(
                podName, options.Namespace, cancellationToken: cancellationToken);
            if (pod.Status?.Phase == PodPhaseRunning) return;
            if (pod.Status?.Phase is PodPhaseFailed or PodPhaseSucceeded)
                throw new InvalidOperationException("Kubernetes sandbox target stopped before it became ready.");
            await Task.Delay(250, cancellationToken);
        }
    }

    private static async Task<int> ReadExecStatusAsync(Stream error, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(error);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content)) return 0;
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("status", out var status)
            && string.Equals(status.GetString(), "Success", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (document.RootElement.TryGetProperty("details", out var details)
            && details.TryGetProperty("causes", out var causes))
        {
            foreach (var cause in causes.EnumerateArray())
            {
                if (cause.TryGetProperty("reason", out var reason)
                    && reason.GetString() == ExternalReasonExitCode
                    && cause.TryGetProperty("message", out var message)
                    && int.TryParse(message.GetString(), out var exitCode))
                    return exitCode;
            }
        }
        throw new InvalidOperationException("Kubernetes exec ended without an exit code.");
    }

    private static bool HasResourceIdentity(
        IDictionary<string, string>? labels,
        RuntimeResourceIdentity identity) =>
        labels is not null
        && labels.TryGetValue("noctf.io/managed", out var managed)
        && string.Equals(managed, "true", StringComparison.Ordinal)
        && labels.TryGetValue("noctf.io/runtime-instance-id", out var runtimeText)
        && Guid.TryParse(runtimeText, out var runtimeId)
        && runtimeId == identity.RuntimeInstanceId
        && labels.TryGetValue("noctf.io/generation", out var generationText)
        && int.TryParse(generationText, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var generation)
        && generation == identity.Generation;

    private static bool HasNetworkPurpose(
        IDictionary<string, string>? labels,
        string purpose) =>
        labels is not null
        && labels.TryGetValue("noctf.io/network-purpose", out var value)
        && string.Equals(value, purpose, StringComparison.Ordinal);

    private static bool HasLabel(
        IDictionary<string, string>? labels,
        string key,
        string value) =>
        labels is not null
        && labels.TryGetValue(key, out var actual)
        && string.Equals(actual, value, StringComparison.Ordinal);

    private static string JobKind(ContainerNetworkPurpose purpose) => purpose switch
    {
        ContainerNetworkPurpose.AwdChecker => NetworkPurposeAwdChecker,
        ContainerNetworkPurpose.AwdpVerification => NetworkPurposeAwdpVerification,
        ContainerNetworkPurpose.PersistentRuntime => NetworkPurposePersistentRuntime,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };
}
