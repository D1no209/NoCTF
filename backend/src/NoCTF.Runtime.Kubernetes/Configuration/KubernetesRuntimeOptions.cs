namespace NoCTF.Runtime.Kubernetes.Configuration;

public sealed record KubernetesRuntimeOptions(
    string Namespace = "noctf",
    string PublicHost = "localhost",
    string CallbackPodLabelKey = "noctf.io/internal-role",
    string CallbackPodLabelValue = "awdp-callback",
    long PodPidsLimit = 0,
    string ClusterDomain = "",
    string ClusterDnsServiceAddress = "",
    bool NetworkPolicyRequired = true,
    IReadOnlyList<string>? ProtectedCidrs = null,
    string CallbackNamespaceLabelKey = "kubernetes.io/metadata.name",
    string CallbackNamespaceLabelValue = "noctf")
{
    public const string PodPidsLimitNodeLabel = "noctf.io/pod-pids-limit";
}
