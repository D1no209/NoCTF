using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Domain.Runtime;

internal static class RuntimeReceiptTestData
{
    internal static ContainerRuntimeReceipt ContainerEntity(
        Guid runtimeInstanceId = default,
        RuntimeProvider provider = RuntimeProvider.Docker) => new()
    {
        RuntimeInstanceId = runtimeInstanceId,
        OperationId = runtimeInstanceId == Guid.Empty ? Guid.CreateVersion7() : runtimeInstanceId,
        Provider = provider,
        ResourceId = "test-runtime",
        Status = RuntimeStatus.Running,
        InternalHost = "runtime.internal"
    };

    internal static ContainerRuntimeReceiptData ContainerData(
        Guid runtimeInstanceId = default,
        RuntimeProvider provider = RuntimeProvider.Docker) => new(
        runtimeInstanceId == Guid.Empty ? Guid.CreateVersion7() : runtimeInstanceId,
        provider,
        "test-runtime",
        RuntimeStatus.Running,
        new Dictionary<int, int>(),
        null,
        "runtime.internal",
        null,
        runtimeInstanceId);
}
