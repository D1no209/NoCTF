using k8s;
using k8s.Models;
using System.Text.Json;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Networking;

namespace NoCTF.Runtime.Kubernetes.Containers;

/// <summary>Runs isolated challenge pods through the Kubernetes client seam.</summary>
public sealed class KubernetesContainerLifecycle(
    IKubernetes client,
    KubernetesRuntimeOptions options) : IContainerLifecycle, IOneShotJobRunner,
    IContainerSandboxLifecycle, IRuntimeResourceReaper
{
    public RuntimeProvider Provider => RuntimeProvider.Kubernetes;
    private const int ExecTimeoutExitCode = 124;
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

        var name = $"noctf-{request.OperationId:N}";
        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        labels["noctf.io/runtime-id"] = name;
        if (request.NetworkName is not null) labels["noctf.io/sandbox"] = request.NetworkName;
        if (request.AllowInternalCallback)
        {
            labels["noctf.io/managed"] = "true";
            labels["noctf.io/job-kind"] = "awdp-verification";
            labels["noctf.io/runtime-instance-id"] = (request.RuntimeInstanceId
                ?? request.OperationId).ToString("D");
            labels["noctf.io/generation"] = request.Generation.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        if (request.AllowInternalCallback && request.Ttl is not null)
            labels["noctf.io/expires-at"] = DateTimeOffset.UtcNow.Add(request.Ttl.Value)
                .ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
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
                                Add = request.Security.CapAdd.ToList()
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
        try
        {
            await client.CoreV1.CreateNamespacedPodAsync(
                pod, options.Namespace, cancellationToken: cancellationToken);
            if (request.AllowInternalCallback)
                await EnsureInternalCallbackPolicyAsync(name, labels, request, cancellationToken);
            var internalHost = await EnsureServiceAsync(name, labels, request, cancellationToken);
            return new(request.OperationId, RuntimeProvider.Kubernetes, name, RuntimeStatus.Pending,
                request.PortMappings, options.PublicHost, internalHost ?? $"{name}.{options.Namespace}.svc");
        }
        catch
        {
            using var cleanupSource = new CancellationTokenSource(
                RunnerScoringCallbackDeliveryPolicy.OneShotCleanupBudget);
            try
            {
                await DestroyAsync(new ContainerReceipt(
                    request.OperationId,
                    RuntimeProvider.Kubernetes,
                    name,
                    RuntimeStatus.Failed,
                    request.PortMappings,
                    options.PublicHost,
                    null), cleanupSource.Token);
            }
            catch
            {
                // Expiry labels and the resource reaper remain the final cleanup fallback.
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
                cancellationToken);
            _ = await CreateAsync(request, cancellationToken);
        }

        var labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        labels["noctf.io/runtime-id"] = name;
        if (request.NetworkName is not null) labels["noctf.io/sandbox"] = request.NetworkName;
        var internalHost = await EnsureServiceAsync(name, labels, request, cancellationToken);
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
                request.PortMappings,
                options.PublicHost,
                internalHost), cancellationToken);
            throw new TimeoutException("Kubernetes runtime did not reach Running before the operation deadline.");
        }
        return new ContainerReceipt(
            request.OperationId,
            RuntimeProvider.Kubernetes,
            name,
            RuntimeStatus.Running,
            request.PortMappings,
            options.PublicHost,
            internalHost ?? $"{name}.{options.Namespace}.svc");
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await TryDeleteAsync(() => client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                $"{receipt.ResourceId}-callback",
                options.Namespace,
                body: new V1DeleteOptions(),
                cancellationToken: cancellationToken));
        await TryDeleteAsync(() => client.CoreV1.DeleteNamespacedServiceAsync(
            receipt.ResourceId,
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
                if (pod.Status?.Phase is "Succeeded" or "Failed")
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

    public async Task<string> CreateIsolatedNetworkAsync(
        ContainerNetworkPolicyRequest request,
        DateTimeOffset expiresAt,
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
            ? "awdp-verification"
            : "persistent-runtime";
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
        if (request.Purpose == ContainerNetworkPurpose.AwdpVerification)
        {
            labels["noctf.io/expires-at"] = expiresAt.ToUnixTimeSeconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
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
                // The ownership-labelled reaper is the final fallback.
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

    public async Task<RuntimeResourceReapResult> ReapExpiredAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string selector = "noctf.io/managed=true,noctf.io/expires-at";
        var removed = 0;
        var failed = 0;
        var pods = await client.CoreV1.ListNamespacedPodAsync(
            options.Namespace, labelSelector: selector, cancellationToken: cancellationToken);
        foreach (var pod in pods.Items.Where(item => IsOwnedExpired(item.Metadata.Labels, now)))
            await TryDeleteAsync(async () => { await client.CoreV1.DeleteNamespacedPodAsync(
                pod.Metadata.Name, options.Namespace, body: new V1DeleteOptions(), cancellationToken: cancellationToken); });
        var services = await client.CoreV1.ListNamespacedServiceAsync(
            options.Namespace, labelSelector: selector, cancellationToken: cancellationToken);
        foreach (var service in services.Items.Where(item => IsOwnedExpired(item.Metadata.Labels, now)))
            await TryDeleteAsync(async () => { await client.CoreV1.DeleteNamespacedServiceAsync(
                service.Metadata.Name, options.Namespace, body: new V1DeleteOptions(), cancellationToken: cancellationToken); });
        var policies = await client.NetworkingV1.ListNamespacedNetworkPolicyAsync(
            options.Namespace, labelSelector: selector, cancellationToken: cancellationToken);
        foreach (var policy in policies.Items.Where(item => IsOwnedExpired(item.Metadata.Labels, now)))
            await TryDeleteAsync(async () => { await client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                policy.Metadata.Name, options.Namespace, body: new V1DeleteOptions(), cancellationToken: cancellationToken); });
        return new(removed, failed);

        async Task TryDeleteAsync(Func<Task> delete)
        {
            try
            {
                await delete();
                removed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed++;
            }
        }
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

    private async Task<string?> EnsureServiceAsync(
        string name,
        IReadOnlyDictionary<string, string> labels,
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        if (request.NetworkName is null || request.ContainerPorts.Count == 0)
            return null;

        try
        {
            var existing = await client.CoreV1.ReadNamespacedServiceAsync(
                name,
                options.Namespace,
                cancellationToken: cancellationToken);
            return existing.Spec.ClusterIP;
        }
        catch (k8s.Autorest.HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var service = await client.CoreV1.CreateNamespacedServiceAsync(new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = name,
                    NamespaceProperty = options.Namespace,
                    Labels = labels.ToDictionary(pair => pair.Key, pair => pair.Value)
                },
                Spec = new V1ServiceSpec
                {
                    Selector = new Dictionary<string, string> { ["noctf.io/runtime-id"] = name },
                    Ports = request.ContainerPorts.Select(port => new V1ServicePort
                    {
                        Name = $"tcp-{port}",
                        Port = port,
                        TargetPort = port
                    }).ToList(),
                    Type = "ClusterIP"
                }
            }, options.Namespace, cancellationToken: cancellationToken);
            return service.Spec.ClusterIP;
        }
    }

    private async Task EnsureInternalCallbackPolicyAsync(
        string name,
        IReadOnlyDictionary<string, string> labels,
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        var callbackPort = GetCallbackPort(request);
        await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta
            {
                Name = $"{name}-callback",
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
                        ["noctf.io/purpose"] = "awdp-checker"
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
        }, options.Namespace, cancellationToken: cancellationToken);
    }

    private static int GetCallbackPort(ContainerRequest request)
    {
        if (!request.Environment.TryGetValue("NOCTF_CALLBACK_URL", out var callbackText)
            || !Uri.TryCreate(callbackText, UriKind.Absolute, out var callback)
            || callback.Scheme is not ("http" or "https"))
            throw new InvalidOperationException(
                "An AWDP checker callback requires an absolute HTTP(S) callback URL.");
        return callback.Port;
    }

    private async Task WaitUntilDeletedAsync(
        string name,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var podExists = true;
            var serviceExists = true;
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
            if (!podExists && !serviceExists)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        throw new TimeoutException("Kubernetes terminal resources were not deleted before recreation.");
    }

    private async Task WaitUntilRunningAsync(string podName, CancellationToken cancellationToken)
    {
        while (true)
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(
                podName, options.Namespace, cancellationToken: cancellationToken);
            if (pod.Status?.Phase == "Running") return;
            if (pod.Status?.Phase is "Failed" or "Succeeded")
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
                    && reason.GetString() == "ExitCode"
                    && cause.TryGetProperty("message", out var message)
                    && int.TryParse(message.GetString(), out var exitCode))
                    return exitCode;
            }
        }
        throw new InvalidOperationException("Kubernetes exec ended without an exit code.");
    }

    private static bool IsOwnedExpired(IDictionary<string, string>? labels, DateTimeOffset now) =>
        labels is not null
        && labels.TryGetValue("noctf.io/managed", out var managed)
        && string.Equals(managed, "true", StringComparison.Ordinal)
        && labels.TryGetValue("noctf.io/job-kind", out var jobKind)
        && jobKind is "awdp-verification" or "persistent-runtime"
        && labels.TryGetValue("noctf.io/runtime-instance-id", out var runtimeText)
        && Guid.TryParse(runtimeText, out var runtimeId)
        && runtimeId != Guid.Empty
        && labels.TryGetValue("noctf.io/generation", out var generationText)
        && int.TryParse(generationText, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var generation)
        && generation > 0
        && labels.TryGetValue("noctf.io/expires-at", out var text)
        && long.TryParse(text, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var expiresAt)
        && expiresAt <= now.ToUnixTimeSeconds();

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
}
