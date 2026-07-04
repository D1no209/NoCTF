using Microsoft.Extensions.Configuration;

namespace NoCTF.Container.K8s;

public sealed class KubernetesRunnerOptions
{
    public string? KubeConfigPath { get; set; }
    public string NamespacePrefix { get; set; } = "noctf-inst";
    public string? PublicEntry { get; set; }
    public string DefaultExposure { get; set; } = "NodePort";
    public string? IngressBaseDomain { get; set; }
    public string? IngressClassName { get; set; }
    public string? IngressTlsSecretName { get; set; }
    public string CpuRequest { get; set; } = "50m";
    public string MemoryRequest { get; set; } = "64Mi";
    public string CpuLimit { get; set; } = "500m";
    public string MemoryLimit { get; set; } = "256Mi";
    public string EphemeralStorageLimit { get; set; } = "1Gi";
    public string NamespaceCpuLimit { get; set; } = "2";
    public string NamespaceMemoryLimit { get; set; } = "2Gi";
    public string NamespacePodLimit { get; set; } = "16";
    public string NetworkMode { get; set; } = "Isolated";
    public string[] DnsServers { get; set; } = [];
    public string[] ImagePullSecrets { get; set; } = [];
    public List<KubernetesRegistryCredential> Registries { get; set; } = [];

    public static KubernetesRunnerOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new KubernetesRunnerOptions();
        configuration.GetSection("K8s").Bind(options);
        configuration.GetSection("Kubernetes").Bind(options);
        return options;
    }
}

public sealed class KubernetesRegistryCredential
{
    public string Registry { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Email { get; set; }
}
