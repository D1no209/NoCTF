using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Provisioning;

/// <summary>Owns the lifecycle of a complete, platform-authored named-service environment.</summary>
public abstract class NamedContainerRuntime(
    IContainerLifecycle containers,
    IContainerSandboxLifecycle execution,
    TimeProvider? clock = null) : IContainerRuntime
{
    protected readonly TimeProvider Clock = clock ?? TimeProvider.System;
    protected abstract RuntimeProvider Provider { get; }
    protected abstract string PublicHost { get; }
    protected abstract string Namespace { get; }
    protected abstract Task<RuntimeNetworkAttachment> PrepareNetworkAsync(ContainerRuntimeRequest request, CancellationToken cancellationToken);
    protected abstract Task RemoveNetworkAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken);

    public async Task<ContainerDeploymentReceipt> UpAsync(ContainerRuntimeRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider != Provider) throw new RuntimeConfigurationException("Runtime provider does not match the adapter.");
        var errors = ContainerRuntimeDefinitionPolicy.Validate(new(request.Services, request.EgressPolicy),
            request.UrlBindings, request.ControlCheckUrlBinding, request.AwdCheckerTargetBinding);
        if (errors.Count > 0) throw new RuntimeConfigurationException(string.Join(" ", errors));
        if (request.Limits != NoCTF.Application.Runtime.Capacity.RuntimeResourceBudgetPolicy.Sum(request.ServiceResources.Values))
            throw new RuntimeConfigurationException("Service resources do not match the committed Runtime allocation.");
        var cleanupReceipt = new ContainerDeploymentReceipt(request.OperationId, Provider, request.ProjectName, Namespace,
            PublicHost, Clock.GetUtcNow(), request.Services.Select(service => new ContainerServiceStatus(service.Name,
                ResourceName(request.OperationId, service.Name), RuntimeStatus.Pending, new Dictionary<int, int>(), null)).ToArray(),
            null, null);
        try
        {
            var network = await PrepareNetworkAsync(request, cancellationToken);
            cleanupReceipt = cleanupReceipt with { OwnedNetworkId = network.OwnedNetworkId, DiscoveryServiceName = network.DiscoveryServiceName };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.OperationTimeout);
            var services = await Task.WhenAll(request.Services.Select(async service =>
            {
                var direct = request.AccessMode != RuntimeAccessMode.WsrxOnly;
                var publicPorts = direct ? (request.UrlBindings ?? []).Where(binding => binding.ServiceName == service.Name)
                    .Select(binding => binding.ContainerPort!.Value).Distinct().ToDictionary(port => port, _ => 0) : new Dictionary<int, int>();
                var internalPorts = (service.InternalPorts ?? []).Concat((request.UrlBindings ?? [])
                    .Where(binding => binding.ServiceName == service.Name).Select(binding => binding.ContainerPort!.Value))
                    .Concat(request.ControlCheckUrlBinding?.ServiceName == service.Name
                        ? [request.ControlCheckUrlBinding.ContainerPort!.Value] : []).Distinct().ToArray();
                var labels = new Dictionary<string, string>(request.Labels, StringComparer.Ordinal)
                {
                    ["noctf.io/managed"] = "true",
                    ["noctf.io/runtime-instance-id"] = request.OperationId.ToString("D"),
                    ["noctf.io/service-name"] = service.Name,
                    ["noctf.io/operation-id"] = request.OperationId.ToString("N")
                };
                if (request.ControlCheckUrlBinding?.ServiceName == service.Name || request.AccessMode != RuntimeAccessMode.Direct)
                    labels["noctf.io/runtime-proxy-target"] = "true";
                var receipt = await containers.EnsureRunningAsync(new ContainerRequest(request.OperationId, Provider, service.Image,
                    service.Command ?? [], service.Environment ?? new Dictionary<string, string>(), labels, publicPorts,
                    request.ServiceResources[service.Name], request.Ttl, NetworkName: network.AttachmentName,
                    OperationTimeout: request.OperationTimeout, InternalPorts: internalPorts, AllowInternalCallback: request.AllowInternalCallback,
                    RuntimeInstanceId: request.OperationId, ControlCheckUrlBinding: request.ControlCheckUrlBinding?.ServiceName == service.Name ? request.ControlCheckUrlBinding : null, EgressPolicy: request.EgressPolicy, NetworkPurpose: request.Purpose,
                    AccessMode: request.AccessMode, Arguments: service.Arguments, ServiceName: service.Name,
                    RegisterServiceAlias: request.Services.Count > 1, DiscoveryServiceName: network.DiscoveryServiceName), timeout.Token);
                return new ContainerServiceStatus(service.Name, receipt.ResourceId, receipt.Status, receipt.PortMappings, receipt.InternalHost);
            }));
            if (services.Any(service => service.Status != RuntimeStatus.Running))
                throw new InvalidOperationException("Every Runtime service must be running before publication.");
            return cleanupReceipt with { Services = services };
        }
        catch (Exception provisionFailure)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await DownAsync(cleanupReceipt, RuntimeTerminationMode.Force, RuntimeTerminationPolicy.Default, cleanup.Token); }
            catch (Exception cleanupFailure) { throw new InvalidOperationException("Runtime provisioning and cleanup failed.", new AggregateException(provisionFailure, cleanupFailure)); }
            if (provisionFailure is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException("Container Runtime startup exceeded its operation timeout.", provisionFailure);
            if (provisionFailure is not (RuntimeConfigurationException or InvalidOperationException or TimeoutException or OperationCanceledException))
                throw new InvalidOperationException("The provider rejected Runtime provisioning.", provisionFailure);
            throw;
        }
    }

    public Task DownAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken) =>
        DownAsync(receipt, RuntimeTerminationMode.GracefulThenForce, RuntimeTerminationPolicy.Default, cancellationToken);

    public async Task DownAsync(ContainerDeploymentReceipt receipt, RuntimeTerminationMode mode, RuntimeTerminationPolicy policy, CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        await Task.WhenAll(receipt.Services.Select(service => containers.DestroyAsync(ServiceReceipt(receipt, service), mode, policy, cancellationToken)));
        foreach (var service in receipt.Services)
            if (await containers.GetAsync(Provider, service.ResourceId, cancellationToken) is not null)
                throw new InvalidOperationException("Runtime service remains after cleanup.");
        using var networkCleanup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        networkCleanup.CancelAfter(policy.NetworkCleanupTimeout + policy.VerificationTimeout);
        try { await RemoveNetworkAsync(receipt, networkCleanup.Token); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("Owned Runtime network resources remain after the cleanup deadline.", exception); }
    }

    public async Task<ContainerRuntimeStatus?> GetStatusAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var services = await Task.WhenAll(receipt.Services.Select(async service =>
        {
            var resource = await containers.GetAsync(Provider, service.ResourceId, cancellationToken);
            return service with { Status = resource?.Status ?? RuntimeStatus.Stopped,
                InternalHost = resource?.InternalHost ?? service.InternalHost };
        }));
        return new(receipt.ProjectName, services.All(service => service.Status == RuntimeStatus.Running)
            ? RuntimeStatus.Running : RuntimeStatus.Failed, services);
    }

    public Task<ContainerExecResult> ExecAsync(ContainerDeploymentReceipt receipt, string serviceName, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var service = receipt.Services.SingleOrDefault(service => service.Name == serviceName)
            ?? throw new RuntimeConfigurationException("The target service does not exist.");
        return execution.ExecAsync(ServiceReceipt(receipt, service), command, timeout, cancellationToken);
    }

    public static string ResourceName(Guid runtimeId, string serviceName)
    {
        var suffix = serviceName.Length <= 24 ? serviceName : serviceName[..15] + "-"
            + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serviceName)))[..8];
        return $"noctf-{runtimeId:N}-{suffix}";
    }
    public static ContainerReceipt ServiceReceipt(ContainerDeploymentReceipt receipt, ContainerServiceStatus service) =>
        new(receipt.OperationId, receipt.Provider, service.ResourceId, service.Status, service.PublishedPorts,
            receipt.PublicHost, service.InternalHost, RuntimeInstanceId: receipt.OperationId);

    private void ValidateReceipt(ContainerDeploymentReceipt receipt)
    {
        if (receipt.Provider != Provider || receipt.OperationId == Guid.Empty || receipt.Services.Count == 0
            || receipt.Namespace != Namespace || receipt.ProjectName != $"noctf-rt-{receipt.OperationId:N}")
            throw new InvalidOperationException("Runtime receipt does not match the provider assignment.");
    }
}

public sealed record RuntimeNetworkAttachment(string AttachmentName, string? OwnedNetworkId = null, string? DiscoveryServiceName = null);
