using System.Net;
using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Observability;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Runtime.Docker;

public sealed class DockerRuntimeResourceReconciler(
    DockerRuntimeOptions options,
    DockerComposeRuntime compose) : IRuntimeManagedResourceReconciler,
    IRuntimeProviderAvailabilityProbe, IRuntimeProxyNetworkReconciler, IDisposable
{
    private readonly DockerClient client = new DockerClientBuilder()
        .WithEndpoint(new Uri(options.Endpoint))
        .Build();

    public RuntimeProvider Provider => RuntimeProvider.Docker;

    public async Task EnsureProxyNetworkAsync(
        Guid runtimeInstanceId,
        RuntimeKind runtimeKind,
        RuntimeReceiptData providerReceipt,
        CancellationToken cancellationToken)
    {
        if (runtimeInstanceId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(runtimeInstanceId));
        if (runtimeKind == RuntimeKind.Compose)
        {
            var receipt = (providerReceipt as ComposeRuntimeReceiptData)?.ToReceipt()
                ?? throw new InvalidOperationException(
                    "Docker Compose Runtime receipt is invalid.");
            await compose.EnsureRuntimeProxyGatewaysAsync(receipt, cancellationToken);
            return;
        }
        if (runtimeKind != RuntimeKind.Container)
            throw new InvalidOperationException(
                "Docker Runtime proxy reconciliation supports Container and Compose only.");
        var container = (providerReceipt as ContainerRuntimeReceiptData)?.ToReceipt()
            ?? throw new InvalidOperationException("Docker Container Runtime receipt is invalid.");
        if (container.RuntimeInstanceId != runtimeInstanceId
            || string.IsNullOrWhiteSpace(container.NetworkId))
            throw new InvalidOperationException(
                "Docker Container Runtime receipt has no owned proxy network.");
        await ConnectRuntimeProxyGatewaysAsync(container.NetworkId, cancellationToken);
    }

    public async Task<bool?> WorkloadExistsAsync(RuntimeWorkloadIdentity identity, CancellationToken cancellationToken)
    {
        identity.Validate();
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true, Filters = IdentityFilters(new(identity.RuntimeInstanceId))
        }, cancellationToken);
        if (containers.Any(container => container.Names.Contains($"/noctf-{identity.OperationId:N}")
            || container.Labels.TryGetValue("noctf.io/operation-id", out var operation)
                && operation == identity.OperationId.ToString("N")))
            return true;
        var networks = await client.Networks.ListNetworksAsync(new NetworksListParameters
        {
            Filters = IdentityFilters(new(identity.RuntimeInstanceId))
        }, cancellationToken);
        return networks.Any(network => network.Name == $"noctf-callback-{identity.OperationId:N}");
    }

    public async Task CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        await client.System.PingAsync(cancellationToken);

    public async Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
        CancellationToken cancellationToken)
    {
        var identities = new HashSet<RuntimeResourceIdentity>();
        var filters = ManagedRuntimeFilters();
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true, Filters = filters },
            cancellationToken);
        foreach (var container in containers)
        {
            if (TryReadIdentity(container.Labels, out var identity))
                identities.Add(identity);
        }

        var networks = await client.Networks.ListNetworksAsync(
            new NetworksListParameters { Filters = filters },
            cancellationToken);
        foreach (var network in networks)
        {
            if (TryReadIdentity(network.Labels, out var identity))
                identities.Add(identity);
        }

        identities.UnionWith(await compose.ListManagedAsync(cancellationToken));
        return identities.ToArray();
    }

    public async Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken)
    {
        await DestroyByIdentityAsync(
            identity,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public async Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        if (identity.RuntimeInstanceId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(identity));
        var warnings = new List<Exception>();
        try
        {
            await compose.DestroyByIdentityAsync(identity, mode, policy, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add(exception);
        }

        var forceStarted = Stopwatch.GetTimestamp();
        using var force = CreateStageToken(cancellationToken, policy.ForceDeleteTimeout);
        var filters = IdentityFilters(identity);
        try
        {
            var containers = await client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true, Filters = filters },
                force.Token);
            foreach (var container in containers.Where(container =>
                         TryReadIdentity(container.Labels, out var actual)
                         && actual == identity))
            {
                try
                {
                    await client.Containers.RemoveContainerAsync(
                        container.ID,
                        new ContainerRemoveParameters { Force = true },
                        force.Token);
                }
                catch (DockerContainerNotFoundException)
                {
                    // Exact cleanup is idempotent.
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    warnings.Add(exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add(exception);
        }
        NoCtfTelemetry.RecordRuntimeStopDuration(
            "docker", "managed", "force_delete",
            warnings.Count == 0 ? "success" : "warning",
            Stopwatch.GetElapsedTime(forceStarted).TotalSeconds);

        var networkStarted = Stopwatch.GetTimestamp();
        using var networkCleanup = CreateStageToken(
            cancellationToken,
            policy.NetworkCleanupTimeout);
        try
        {
            var networks = await client.Networks.ListNetworksAsync(
                new NetworksListParameters { Filters = filters },
                networkCleanup.Token);
            foreach (var network in networks.Where(network =>
                         TryReadIdentity(network.Labels, out var actual)
                         && actual == identity))
            {
                try
                {
                    var current = await client.Networks.InspectNetworkAsync(
                        network.ID,
                        networkCleanup.Token);
                    var gateways = await ResolveRuntimeProxyGatewaysAsync(
                        networkCleanup.Token,
                        requireAny: false);
                    foreach (var gateway in gateways)
                    {
                        if (current.Containers?.ContainsKey(gateway) != true)
                            continue;
                        await client.Networks.DisconnectNetworkAsync(
                            current.ID,
                            new NetworkDisconnectParameters
                            {
                                Container = gateway,
                                Force = true
                            },
                            networkCleanup.Token);
                    }
                    await client.Networks.DeleteNetworkAsync(network.ID, networkCleanup.Token);
                }
                catch (DockerApiException exception)
                    when (exception.StatusCode == HttpStatusCode.NotFound)
                {
                    // Exact cleanup is idempotent.
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    warnings.Add(exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add(exception);
        }
        NoCtfTelemetry.RecordRuntimeStopDuration(
            "docker", "managed", "network_cleanup",
            warnings.Count == 0 ? "success" : "warning",
            Stopwatch.GetElapsedTime(networkStarted).TotalSeconds);

        var verificationStarted = Stopwatch.GetTimestamp();
        using var verification = CreateStageToken(
            cancellationToken,
            policy.VerificationTimeout);
        try
        {
            await WaitUntilDeletedAsync(identity, verification.Token);
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "managed", "verification", "success",
                Stopwatch.GetElapsedTime(verificationStarted).TotalSeconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            NoCtfTelemetry.RecordRuntimeStopResourcesRemaining("docker", "managed");
            NoCtfTelemetry.RecordRuntimeStopDuration(
                "docker", "managed", "verification", "timeout",
                Stopwatch.GetElapsedTime(verificationStarted).TotalSeconds);
            throw new InvalidOperationException(
                "Docker Runtime resources remain after identity-based cleanup.",
                warnings.Count == 0 ? null : new AggregateException(warnings));
        }
    }

    private async Task WaitUntilDeletedAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken)
    {
        var delays = new[] { 200, 400, 800, 1_000 };
        var attempt = 0;
        while ((await ListManagedAsync(cancellationToken)).Contains(identity))
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(delays[Math.Min(attempt++, delays.Length - 1)]),
                cancellationToken);
        }
    }

    private async Task ConnectRuntimeProxyGatewaysAsync(
        string networkId,
        CancellationToken cancellationToken)
    {
        var network = await client.Networks.InspectNetworkAsync(networkId, cancellationToken);
        var gateways = await ResolveRuntimeProxyGatewaysAsync(cancellationToken);
        foreach (var gateway in gateways)
        {
            if (network.Containers?.ContainsKey(gateway) == true)
                continue;
            await client.Networks.ConnectNetworkAsync(
                network.ID,
                new NetworkConnectParameters { Container = gateway },
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> ResolveRuntimeProxyGatewaysAsync(
        CancellationToken cancellationToken,
        bool requireAny = true)
    {
        if (!string.IsNullOrWhiteSpace(options.ProxyContainerName))
        {
            var configured = await client.Containers.InspectContainerAsync(
                options.ProxyContainerName,
                cancellationToken);
            EnsureProxyRole(configured.ID, configured.Config?.Labels);
            return [configured.ID];
        }
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{options.ProxyContainerLabelKey}={options.ProxyContainerLabelValue}"] = true
                    }
                }
            },
            cancellationToken);
        if (requireAny && containers.Count == 0)
            throw new InvalidOperationException(
                "No running Runtime proxy gateway carries the required role label.");
        foreach (var container in containers)
            EnsureProxyRole(container.ID, container.Labels);
        return containers.Select(container => container.ID)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private void EnsureProxyRole(
        string containerId,
        IDictionary<string, string>? labels)
    {
        if (string.IsNullOrWhiteSpace(containerId)
            || labels is null
            || !labels.TryGetValue(options.ProxyContainerLabelKey, out var value)
            || !string.Equals(value, options.ProxyContainerLabelValue, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The configured Runtime proxy gateway does not carry the required role label.");
    }

    private static CancellationTokenSource CreateStageToken(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    public void Dispose() => client.Dispose();

    private static Dictionary<string, IDictionary<string, bool>> ManagedRuntimeFilters() =>
        new()
        {
            ["label"] = new Dictionary<string, bool>
            {
                ["noctf.io/managed=true"] = true
            }
        };

    private static Dictionary<string, IDictionary<string, bool>> IdentityFilters(
        RuntimeResourceIdentity identity) =>
        new()
        {
            ["label"] = new Dictionary<string, bool>
            {
                ["noctf.io/managed=true"] = true,
                [$"noctf.io/runtime-instance-id={identity.RuntimeInstanceId:D}"] = true
            }
        };

    private static bool TryReadIdentity(
        IDictionary<string, string>? labels,
        out RuntimeResourceIdentity identity)
    {
        identity = default;
        if (labels is null
            || !labels.TryGetValue("noctf.io/managed", out var managed)
            || !string.Equals(managed, "true", StringComparison.Ordinal)
            || !labels.TryGetValue("noctf.io/runtime-instance-id", out var runtimeText)
            || !Guid.TryParse(runtimeText, out var runtimeId)
            || runtimeId == Guid.Empty)
            return false;
        identity = new(runtimeId);
        return true;
    }
}
