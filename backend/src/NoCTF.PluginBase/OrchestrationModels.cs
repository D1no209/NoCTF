namespace NoCTF.PluginBase;

public enum OrchestrationProvider
{
    Docker,
    Kubernetes
}

public enum OrchestrationRuntimeKind
{
    SingleContainer,
    Compose
}

public enum OrchestrationExposureType
{
    None,
    NodePort,
    ClusterIP,
    Ingress
}

public enum OrchestrationNetworkMode
{
    Isolated,
    Open
}

public sealed class OrchestrationSpec
{
    public OrchestrationProvider Provider { get; set; } = OrchestrationProvider.Docker;
    public OrchestrationRuntimeKind Runtime { get; set; } = OrchestrationRuntimeKind.SingleContainer;
    public string? Image { get; set; }
    public string? Command { get; set; }
    public List<string> Entrypoint { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = [];
    public int? ExposedPort { get; set; }
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public KubernetesOrchestrationSpec Kubernetes { get; set; } = new();
}

public sealed class KubernetesOrchestrationSpec
{
    public OrchestrationExposureType Exposure { get; set; } = OrchestrationExposureType.None;
    public OrchestrationNetworkMode NetworkMode { get; set; } = OrchestrationNetworkMode.Isolated;
    public string ImagePullPolicy { get; set; } = "IfNotPresent";
    public List<string> ImagePullSecrets { get; set; } = [];
    public KubernetesResourceSpec Resources { get; set; } = new();
    public KubernetesSecuritySpec Security { get; set; } = new();
    public KubernetesIngressSpec Ingress { get; set; } = new();
    public Dictionary<string, string> NodeSelector { get; set; } = [];
    public List<KubernetesTolerationSpec> Tolerations { get; set; } = [];
    public Dictionary<string, string> Annotations { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
    public List<KubernetesVolumeSpec> Volumes { get; set; } = [];
}

public sealed class KubernetesResourceSpec
{
    public string CpuRequest { get; set; } = "50m";
    public string MemoryRequest { get; set; } = "64Mi";
    public string CpuLimit { get; set; } = "500m";
    public string MemoryLimit { get; set; } = "256Mi";
    public string EphemeralStorageLimit { get; set; } = "1Gi";
}

public sealed class KubernetesSecuritySpec
{
    public bool AllowPrivilegeEscalation { get; set; }
    public bool AutomountServiceAccountToken { get; set; }
    public bool? RunAsNonRoot { get; set; } = true;
    public bool? ReadOnlyRootFilesystem { get; set; }
    public long? RunAsUser { get; set; } = 1000;
    public long? RunAsGroup { get; set; } = 1000;
    public List<string> CapabilitiesDrop { get; set; } = ["ALL"];
    public List<string> CapabilitiesAdd { get; set; } = [];
}

public sealed class KubernetesIngressSpec
{
    public string? Host { get; set; }
    public string? BaseDomain { get; set; }
    public string? ClassName { get; set; }
    public string? TlsSecretName { get; set; }
    public string Path { get; set; } = "/";
}

public sealed class KubernetesTolerationSpec
{
    public string? Key { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public string? Effect { get; set; }
}

public sealed class KubernetesVolumeSpec
{
    public string Name { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public string Type { get; set; } = "emptyDir";
    public bool ReadOnly { get; set; }
    public Dictionary<string, string> Data { get; set; } = [];
}
