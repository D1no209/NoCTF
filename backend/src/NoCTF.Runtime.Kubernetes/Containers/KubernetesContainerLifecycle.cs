using k8s;
using k8s.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Runtime.Kubernetes.Containers;

/// <summary>Runs isolated challenge pods through the Kubernetes client seam.</summary>
public sealed class KubernetesContainerLifecycle(
    IKubernetes client,
    KubernetesRuntimeOptions options) : IContainerLifecycle
{
    public async Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider != RuntimeProvider.Kubernetes)
            throw new ArgumentOutOfRangeException(nameof(request), request.Provider, "Kubernetes runtime cannot create another provider.");

        var name = $"noctf-{request.OperationId:N}";
        var pod = new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = options.Namespace,
                Labels = request.Labels.ToDictionary(pair => pair.Key, pair => pair.Value)
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
                        Env = request.Environment.Select(pair => new V1EnvVar { Name = pair.Key, Value = pair.Value }).ToList(),
                        Ports = request.PortMappings.Keys.Select(port => new V1ContainerPort { ContainerPort = port }).ToList()
                    }
                ],
                RestartPolicy = "Never"
            }
        };
        await client.CoreV1.CreateNamespacedPodAsync(pod, options.Namespace, cancellationToken: cancellationToken);
        return new(request.OperationId, RuntimeProvider.Kubernetes, name, RuntimeStatus.Pending, request.PortMappings, options.PublicHost, $"{name}.{options.Namespace}.svc");
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            await client.CoreV1.DeleteNamespacedPodAsync(
                receipt.ResourceId,
                options.Namespace,
                body: new V1DeleteOptions { PropagationPolicy = "Foreground" },
                cancellationToken: cancellationToken);
        }
        catch (k8s.Autorest.HttpOperationException exception)
            when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Destroy is idempotent: an externally removed runtime is already stopped.
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
}
