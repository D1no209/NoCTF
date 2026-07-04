using k8s;
using Microsoft.Extensions.Configuration;

namespace NoCTF.Container.K8s;

public sealed class KubernetesProvider : IDisposable
{
    public KubernetesProvider(IConfiguration configuration)
    {
        Options = KubernetesRunnerOptions.FromConfiguration(configuration);
        ClientConfiguration = BuildClientConfiguration(Options);
        Client = new Kubernetes(ClientConfiguration);
        ConnectionMode = !string.IsNullOrWhiteSpace(Options.KubeConfigPath)
            ? "kubeconfig"
            : KubernetesClientConfiguration.IsInCluster()
                ? "in-cluster"
                : "default";
    }

    public IKubernetes Client { get; }
    public KubernetesClientConfiguration ClientConfiguration { get; }
    public KubernetesRunnerOptions Options { get; }
    public string ConnectionMode { get; }

    private static KubernetesClientConfiguration BuildClientConfiguration(KubernetesRunnerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.KubeConfigPath))
        {
            if (!File.Exists(options.KubeConfigPath))
                throw new FileNotFoundException("Configured Kubernetes kubeconfig path was not found.", options.KubeConfigPath);
            return KubernetesClientConfiguration.BuildConfigFromConfigFile(options.KubeConfigPath);
        }

        if (KubernetesClientConfiguration.IsInCluster())
            return KubernetesClientConfiguration.InClusterConfig();

        return KubernetesClientConfiguration.BuildConfigFromConfigFile();
    }

    public void Dispose()
    {
        Client.Dispose();
    }
}
