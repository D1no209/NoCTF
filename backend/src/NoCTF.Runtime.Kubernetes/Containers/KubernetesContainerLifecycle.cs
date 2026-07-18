using k8s;
using k8s.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Runtime.Kubernetes.Containers;

/// <summary>Runs isolated challenge pods through the Kubernetes client seam.</summary>
public sealed class KubernetesContainerLifecycle(
    IKubernetes client,
    KubernetesRuntimeOptions options) : IContainerLifecycle
{
    public async Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken)
    {
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
        return new(request.OperationId, "kubernetes", name, "pending", request.PortMappings, options.PublicHost, $"{name}.{options.Namespace}.svc");
    }

    public async Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken) =>
        await client.CoreV1.DeleteNamespacedPodAsync(
            receipt.ResourceId,
            options.Namespace,
            body: new V1DeleteOptions { PropagationPolicy = "Foreground" },
            cancellationToken: cancellationToken);

    public async Task<ContainerReceipt?> GetAsync(string resourceId, CancellationToken cancellationToken)
    {
        try
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(resourceId, options.Namespace, cancellationToken: cancellationToken);
            var phase = pod.Status?.Phase ?? "Unknown";
            return new(Guid.Empty, "kubernetes", resourceId, phase, new Dictionary<int, int>(), options.PublicHost,
                $"{resourceId}.{options.Namespace}.svc");
        }
        catch (k8s.Autorest.HttpOperationException exception) when (exception.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
