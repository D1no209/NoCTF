using k8s;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Runtime.Kubernetes.Compose;

/// <summary>Owns Kubernetes Compose translation and deployment receipts.</summary>
public sealed class KubernetesComposeRuntime(IKubernetes client, KubernetesRuntimeOptions options) : IComposeRuntime
{
    public Task<ComposeReceipt> UpAsync(ComposeRequest request, CancellationToken cancellationToken)
    {
        _ = client;
        return Task.FromResult(new ComposeReceipt(request.OperationId, RuntimeProvider.Kubernetes, request.ProjectName, options.Namespace, DateTimeOffset.UtcNow));
    }

    public Task DownAsync(ComposeReceipt receipt, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ComposeStatus?> GetStatusAsync(ComposeReceipt receipt, CancellationToken cancellationToken) =>
        Task.FromResult<ComposeStatus?>(new(receipt.ProjectName, RuntimeStatus.Failed, []));
}
