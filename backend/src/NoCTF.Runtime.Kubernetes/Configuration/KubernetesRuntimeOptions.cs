namespace NoCTF.Runtime.Kubernetes.Configuration;

public sealed record KubernetesRuntimeOptions(
    string Namespace = "noctf",
    string PublicHost = "localhost",
    string ImagePullPolicy = "IfNotPresent",
    string CallbackPodLabelKey = "noctf.io/internal-role",
    string CallbackPodLabelValue = "awdp-callback");
