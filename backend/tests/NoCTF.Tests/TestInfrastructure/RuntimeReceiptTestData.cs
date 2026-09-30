using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Domain.Runtime;

internal static class RuntimeReceiptTestData
{
    internal static ContainerRuntimeReceipt ContainerEntity(Guid runtimeInstanceId = default, RuntimeProvider provider = RuntimeProvider.Docker) =>
        (ContainerRuntimeReceipt)ContainerData(runtimeInstanceId, provider).ToEntity(runtimeInstanceId);
    internal static ContainerRuntimeReceiptData ContainerData(Guid runtimeInstanceId = default, RuntimeProvider provider = RuntimeProvider.Docker)
    {
        var id = runtimeInstanceId == Guid.Empty ? Guid.CreateVersion7() : runtimeInstanceId;
        return new(id, provider, $"noctf-rt-{id:N}", provider == RuntimeProvider.Kubernetes ? "noctf" : "", "runner.example", DateTimeOffset.UtcNow,
            [new("main", "test-runtime", RuntimeStatus.Running, new Dictionary<int, int>(), "runtime.internal")]);
    }
    internal static ContainerRuntimeReceiptData From(ContainerReceipt receipt) => ContainerRuntimeReceiptData.From(Deployment(receipt));
    internal static ContainerRuntimeReceiptData From(ContainerDeploymentReceipt receipt) => ContainerRuntimeReceiptData.From(receipt);
    internal static ContainerDeploymentReceipt Deployment(ContainerReceipt receipt) => new(receipt.RuntimeInstanceId ?? receipt.OperationId, receipt.Provider,
        $"noctf-rt-{receipt.RuntimeInstanceId ?? receipt.OperationId:N}", receipt.Provider == RuntimeProvider.Kubernetes ? "noctf" : "", receipt.PublicHost ?? "runner.example",
        DateTimeOffset.UtcNow, [new("main", receipt.ResourceId, receipt.Status, receipt.PortMappings, receipt.InternalHost)], receipt.NetworkId);
    internal static ContainerRuntimeRequest Request(ContainerRequest request)
    {
        var service = new RuntimeServiceDefinition("main", request.Image, request.Limits.CpuMillicores / 1000m,
            Math.Max(1, request.Limits.MemoryBytes / (1024 * 1024)), request.Command, request.Arguments, request.Environment, request.InternalPorts);
        return new(request.OperationId, request.Provider, [service], request.Labels, service.Resources(request.Limits.PidsLimit),
            request.Ttl, request.OperationTimeout ?? TimeSpan.FromMinutes(2), request.UrlBindings, request.ControlCheckUrlBinding,
            request.AwdCheckerTargetBinding, request.EgressPolicy, request.AccessMode);
    }
}
